// Conexión RTMP/RTMPS hacia una plataforma (lado cliente del protocolo, como hace OBS).
//
// Connect: TCP (y TLS si es rtmps://) → saludo → connect → releaseStream/FCPublish →
// createStream → publish, y espera a que la plataforma conteste "NetStream.Publish.Start".
// Después WriteMedia envía cada paquete troceado. Un hilo "rtmp-cliente" lee lo que contesta la
// plataforma (acuses, pings, errores) para detectar enseguida si cierra la emisión.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace StreamDelay
{
	internal class RtmpClient
	{
		// Estado de cada canal de chunks que llega de la plataforma.
		private class InChunk
		{
			public uint Ts;

			public uint Delta;

			public uint Len;

			public byte Type;

			public bool Ext;

			public byte[] Buf;

			public int Got;
		}

		public Action<string> Log = delegate
		{
		};

		// Aceptar certificados no válidos en rtmps:// (nunca se activa; queda para pruebas).
		public static bool SkipCertCheck;

		private TcpClient tcp;

		// raw: la conexión (con TLS si hace falta). net: la misma, con búfer, para leer.
		private Stream raw;

		private Stream net;

		private Thread reader;

		private readonly object writeLock = new object();

		private readonly object sync = new object();

		private readonly byte[] hdr = new byte[16];

		private int outChunk = 128;

		private int inChunk = 128;

		private uint streamId;

		private long bytesIn;

		private long lastAck;

		private uint ackWindow;

		private volatile bool closed;

		private volatile string error;

		// Respuesta de la plataforma a publish ("NetStream.Publish.Start" si la acepta).
		private string publishCode;

		// Respuestas (_result / _error) por número de transacción.
		private readonly Dictionary<double, List<object>> results = new Dictionary<double, List<object>>();

		private readonly Dictionary<int, InChunk> states = new Dictionary<int, InChunk>();

		// "@setDataFrame" en AMF0: va delante de los metadatos que se envían.
		private static readonly byte[] SetDataFrame = new byte[16]
		{
			2, 0, 13, 64, 115, 101, 116, 68, 97, 116,
			97, 70, 114, 97, 109, 101
		};

		// Búfer donde se monta cada mensaje antes de enviarlo (se reutiliza).
		private readonly MemoryStream wbuf = new MemoryStream(65536);

		public bool Alive
		{
			get
			{
				if (!closed)
				{
					return error == null;
				}
				return false;
			}
		}

		public string Error
		{
			get
			{
				return error;
			}
		}

		// Conecta y empieza a publicar con la clave name. Lanza IOException con el motivo si falla.
		public void Connect(string url, string name, int timeoutMs)
		{
			bool tls;
			string host;
			int port;
			string app;
			ParseUrl(url, out tls, out host, out port, out app);
			string tcUrl = (tls ? "rtmps://" : "rtmp://") + host + ":" + port + "/" + app;
			tcp = new TcpClient();
			tcp.NoDelay = true;
			tcp.SendBufferSize = 1048576;
			IAsyncResult attempt = tcp.BeginConnect(host, port, null, null);
			if (!attempt.AsyncWaitHandle.WaitOne(timeoutMs))
			{
				Close();
				throw new IOException("no responde " + host + ":" + port);
			}
			try
			{
				tcp.EndConnect(attempt);
			}
			catch (Exception ex)
			{
				Close();
				throw new IOException("no se pudo conectar con " + host + " (" + ex.Message + ")");
			}
			tcp.ReceiveTimeout = timeoutMs;
			tcp.SendTimeout = 20000;
			Stream stream = tcp.GetStream();
			if (tls)
			{
				SslStream ssl = new SslStream(stream, false, (object o, X509Certificate cert, X509Chain chain, SslPolicyErrors errs) => SkipCertCheck || errs == SslPolicyErrors.None);
				try
				{
					ssl.AuthenticateAsClient(host, null, SslProtocols.Tls12, true);
				}
				catch (Exception ex2)
				{
					Close();
					throw new IOException("fallo de conexión segura (rtmps) con " + host + ": " + ex2.Message);
				}
				stream = ssl;
			}
			raw = stream;
			net = new BufferedStream(stream, 65536);
			Handshake();
			tcp.ReceiveTimeout = 0;
			reader = new Thread(ReadLoop)
			{
				IsBackground = true,
				Name = "rtmp-cliente"
			};
			reader.Start();
			// Trozos de 4096 bytes y connect (se presenta como el codificador de Flash, como OBS).
			SendControl(1, BE32(4096u));
			outChunk = 4096;
			Dictionary<string, object> props = new Dictionary<string, object>();
			props["app"] = app;
			props["type"] = "nonprivate";
			props["flashVer"] = "FMLE/3.0 (compatible; FMSc/1.0)";
			props["swfUrl"] = tcUrl;
			props["tcUrl"] = tcUrl;
			SendCommand(3, 0u, "connect", 1.0, props);
			List<object> reply = Wait(1.0, timeoutMs);
			if (reply == null)
			{
				throw Fail("la plataforma no respondió al conectar");
			}
			if ((string)reply[0] == "_error")
			{
				throw Fail("la plataforma rechazó la conexión (" + Describe(reply) + ")");
			}
			SendCommand(3, 0u, "releaseStream", 2.0, null, name);
			SendCommand(3, 0u, "FCPublish", 3.0, null, name);
			// createStream(null).
			object[] args = new object[1];
			SendCommand(3, 0u, "createStream", 4.0, args);
			reply = Wait(4.0, timeoutMs);
			if (reply == null || (string)reply[0] != "_result")
			{
				throw Fail("la plataforma no abrió el canal de emisión");
			}
			// El id del stream es el último número de la respuesta (1 si no viene).
			streamId = 1u;
			for (int i = reply.Count - 1; i >= 2; i--)
			{
				if (reply[i] is double)
				{
					streamId = (uint)(double)reply[i];
					break;
				}
			}
			SendCommand(4, streamId, "publish", 5.0, null, name, "live");
			// Espera la respuesta a publish (la apunta Command en publishCode).
			lock (sync)
			{
				long deadline = Environment.TickCount + timeoutMs;
				while (publishCode == null && error == null && !closed)
				{
					int left = (int)(deadline - Environment.TickCount);
					if (left > 0)
					{
						Monitor.Wait(sync, left);
						continue;
					}
					break;
				}
			}
			if (publishCode == null || publishCode.IndexOf("Publish.Start", StringComparison.OrdinalIgnoreCase) < 0)
			{
				string why = publishCode ?? error;
				throw Fail((why == null) ? "la plataforma no aceptó la emisión (¿clave incorrecta o caducada?)" : ("la plataforma rechazó la emisión: " + why));
			}
		}

		private IOException Fail(string m)
		{
			Close();
			return new IOException(m);
		}

		// rtmp[s]://servidor[:puerto]/app → partes (puerto por defecto 1935, o 443 con rtmps).
		private static void ParseUrl(string url, out bool tls, out string host, out int port, out string app)
		{
			string u = url.Trim();
			int scheme = u.IndexOf("://", StringComparison.Ordinal);
			if (scheme < 0)
			{
				throw new IOException("servidor no válido: " + url);
			}
			string protocol = u.Substring(0, scheme).ToLowerInvariant();
			tls = protocol == "rtmps";
			if (protocol != "rtmp" && protocol != "rtmps")
			{
				throw new IOException("el servidor debe empezar por rtmp:// o rtmps://");
			}
			string rest = u.Substring(scheme + 3);
			int slash = rest.IndexOf('/');
			string hostPort = ((slash < 0) ? rest : rest.Substring(0, slash));
			app = ((slash < 0) ? "" : rest.Substring(slash + 1).Trim('/'));
			port = (tls ? 443 : 1935);
			// "host:puerto" (cuidando las direcciones IPv6 entre corchetes).
			int colon = hostPort.LastIndexOf(':');
			if (colon > 0 && hostPort.IndexOf(']') < colon)
			{
				int value;
				if (int.TryParse(hostPort.Substring(colon + 1), out value))
				{
					port = value;
				}
				hostPort = hostPort.Substring(0, colon);
			}
			host = hostPort.Trim('[', ']');
		}

		// Saludo simple: C0+C1 aleatorio → S0+S1+S2 → C2 = eco de S1.
		private void Handshake()
		{
			byte[] c0c1 = new byte[1537];
			c0c1[0] = 3;
			Random random = new Random();
			for (int i = 9; i < c0c1.Length; i++)
			{
				c0c1[i] = (byte)random.Next(256);
			}
			raw.Write(c0c1, 0, c0c1.Length);
			byte[] s0s1s2 = new byte[3073];
			ReadExact(s0s1s2, 0, s0s1s2.Length);
			if (s0s1s2[0] != 3)
			{
				throw Fail("respuesta no RTMP del servidor de la plataforma");
			}
			raw.Write(s0s1s2, 1, 1536);
		}

		private void ReadExact(byte[] b, int off, int n)
		{
			while (n > 0)
			{
				int got = net.Read(b, off, n);
				if (got <= 0)
				{
					throw new IOException("la plataforma cerró la conexión");
				}
				off += got;
				n -= got;
				bytesIn += got;
			}
		}

		// Hilo "rtmp-cliente": junta los mensajes de la plataforma (mismo formato de trozos que en
		// RtmpSession). Si la conexión se cae, apunta el error y despierta a quien espere.
		private void ReadLoop()
		{
			try
			{
				while (!closed)
				{
					ReadExact(hdr, 0, 1);
					int first = hdr[0];
					int format = first >> 6;
					int csid = first & 0x3F;
					switch (csid)
					{
					case 0:
						ReadExact(hdr, 0, 1);
						csid = 64 + hdr[0];
						break;
					case 1:
						ReadExact(hdr, 0, 2);
						csid = 64 + hdr[0] + hdr[1] * 256;
						break;
					}
					InChunk st;
					if (!states.TryGetValue(csid, out st))
					{
						st = new InChunk();
						states[csid] = st;
					}
					uint ts24 = 0u;
					if (format <= 2)
					{
						ReadExact(hdr, 0, 3);
						ts24 = (uint)((hdr[0] << 16) | (hdr[1] << 8) | hdr[2]);
					}
					if (format <= 1)
					{
						ReadExact(hdr, 0, 4);
						st.Len = (uint)((hdr[0] << 16) | (hdr[1] << 8) | hdr[2]);
						st.Type = hdr[3];
					}
					if (format == 0)
					{
						// Id de stream: no hace falta.
						ReadExact(hdr, 0, 4);
					}
					if (format <= 2)
					{
						st.Ext = ts24 == 16777215;
					}
					uint extTs = 0u;
					if (st.Ext)
					{
						ReadExact(hdr, 0, 4);
						extTs = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
					}
					if (format <= 2)
					{
						uint delta = (st.Ext ? extTs : ts24);
						uint step = (st.Delta = delta);
						st.Ts = ((format == 0) ? step : (st.Ts + step));
						st.Buf = null;
					}
					else if (st.Buf == null)
					{
						st.Ts += st.Delta;
					}
					if (st.Buf == null)
					{
						// Lo que manda una plataforma son mensajes pequeños: más de 4 MB es un error.
						if (st.Len > 4194304)
						{
							throw new IOException("mensaje demasiado grande de la plataforma");
						}
						st.Buf = new byte[st.Len];
						st.Got = 0;
					}
					int count = Math.Min(inChunk, (int)st.Len - st.Got);
					if (count > 0)
					{
						ReadExact(st.Buf, st.Got, count);
					}
					st.Got += count;
					if (st.Got >= st.Len)
					{
						byte[] message = st.Buf;
						st.Buf = null;
						Handle(st.Type, st.Ts, message);
					}
					if (ackWindow != 0 && bytesIn - lastAck >= ackWindow)
					{
						lastAck = bytesIn;
						SendControl(3, BE32((uint)bytesIn));
					}
				}
			}
			catch (Exception ex)
			{
				if (!closed && error == null)
				{
					error = ((ex is IOException) ? ex.Message : ("conexión perdida (" + ex.Message + ")"));
				}
			}
			finally
			{
				lock (sync)
				{
					Monitor.PulseAll(sync);
				}
			}
		}

		private void Handle(byte type, uint ts, byte[] msg)
		{
			switch (type)
			{
			case 1:
				// Tamaño de trozo.
				if (msg.Length >= 4)
				{
					inChunk = Math.Max(1, (int)(BE32(msg, 0) & 0x7FFFFFFF));
				}
				break;
			case 4:
				// Ping (evento 6): se contesta con un pong (evento 7) con la misma marca.
				if (msg.Length >= 6 && msg[0] == 0 && msg[1] == 6)
				{
					SendControl(4, new byte[6]
					{
						0,
						7,
						msg[2],
						msg[3],
						msg[4],
						msg[5]
					});
				}
				break;
			case 5:
				// Ventana de acuses.
				if (msg.Length >= 4)
				{
					ackWindow = BE32(msg, 0);
				}
				break;
			case 6:
				// Ancho de banda: se contesta con nuestra ventana de acuses.
				SendControl(5, BE32(2500000u));
				break;
			case 20:
				Command(msg);
				break;
			case 17:
				// Comando AMF3 (un byte de más delante).
				if (msg.Length > 1)
				{
					byte[] amf0 = new byte[msg.Length - 1];
					Buffer.BlockCopy(msg, 1, amf0, 0, amf0.Length);
					Command(amf0);
				}
				break;
			}
		}

		// Respuestas de la plataforma: _result/_error de un comando, onStatus de publish o close.
		private void Command(byte[] msg)
		{
			AmfReader reader = new AmfReader(msg);
			List<object> values = new List<object>();
			try
			{
				while (reader.More)
				{
					values.Add(reader.Read());
				}
			}
			catch
			{
			}
			if (values.Count == 0)
			{
				return;
			}
			switch (values[0] as string)
			{
			case "_result":
			case "_error":
			{
				double key = ((values.Count > 1 && values[1] is double) ? ((double)values[1]) : 0.0);
				lock (sync)
				{
					results[key] = values;
					Monitor.PulseAll(sync);
					break;
				}
			}
			case "onStatus":
			{
				Dictionary<string, object> info = null;
				for (int i = 2; i < values.Count; i++)
				{
					if (info != null)
					{
						break;
					}
					info = values[i] as Dictionary<string, object>;
				}
				if (info == null)
				{
					break;
				}
				string code = Get(info, "code");
				string level = Get(info, "level");
				string description = Get(info, "description");
				if (code.IndexOf("Publish.Start", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					lock (sync)
					{
						publishCode = code;
						Monitor.PulseAll(sync);
						break;
					}
				}
				// Solo interesan los errores ("level: error", "…Failed", "…BadName").
				if (!(level == "error") && code.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) < 0 && code.IndexOf("BadName", StringComparison.OrdinalIgnoreCase) < 0)
				{
					break;
				}
				string problem = code + ((description.Length > 0) ? (" — " + description) : "");
				lock (sync)
				{
					// Antes de publicar es la respuesta a publish; después, un error de la emisión.
					if (publishCode == null)
					{
						publishCode = problem;
					}
					else
					{
						error = problem;
					}
					Monitor.PulseAll(sync);
					break;
				}
			}
			case "close":
				error = "la plataforma cerró la emisión";
				lock (sync)
				{
					Monitor.PulseAll(sync);
					break;
				}
			}
		}

		private static string Get(Dictionary<string, object> d, string k)
		{
			object value;
			if (!d.TryGetValue(k, out value) || !(value is string))
			{
				return "";
			}
			return (string)value;
		}

		// "código descripción" del primer objeto de la respuesta que los tenga.
		private static string Describe(List<object> r)
		{
			foreach (object item in r)
			{
				Dictionary<string, object> info = item as Dictionary<string, object>;
				if (info != null)
				{
					string code = Get(info, "code");
					string description = Get(info, "description");
					if (code.Length > 0 || description.Length > 0)
					{
						return (code + " " + description).Trim();
					}
				}
			}
			return "sin detalle";
		}

		// Espera la respuesta a la transacción txn (null si no llega o se corta la conexión).
		private List<object> Wait(double txn, int timeoutMs)
		{
			lock (sync)
			{
				long deadline = Environment.TickCount + timeoutMs;
				List<object> value;
				while (!results.TryGetValue(txn, out value))
				{
					if (error != null || closed)
					{
						return null;
					}
					int left = (int)(deadline - Environment.TickCount);
					if (left <= 0)
					{
						return null;
					}
					Monitor.Wait(sync, left);
				}
				return value;
			}
		}

		// Envía un paquete: metadatos (canal 4, con @setDataFrame delante), audio (canal 5) o vídeo (canal 6).
		public void WriteMedia(byte type, long ts, byte[] data)
		{
			if (error != null)
			{
				throw new IOException(error);
			}
			if (closed)
			{
				throw new IOException("conexión cerrada");
			}
			if (type == 18)
			{
				byte[] meta = new byte[SetDataFrame.Length + data.Length];
				Buffer.BlockCopy(SetDataFrame, 0, meta, 0, SetDataFrame.Length);
				Buffer.BlockCopy(data, 0, meta, SetDataFrame.Length, data.Length);
				SendMessage(4, type, streamId, (uint)ts, meta, 0, meta.Length);
			}
			else
			{
				SendMessage((type == 8) ? 5 : 6, type, streamId, (uint)ts, data, 0, data.Length);
			}
		}

		private void SendControl(byte type, byte[] payload)
		{
			SendMessage(2, type, 0u, 0u, payload, 0, payload.Length);
		}

		private void SendCommand(int csid, uint sid, string name, double txn, params object[] args)
		{
			MemoryStream body = new MemoryStream();
			RtmpSession.AmfWrite(body, name);
			RtmpSession.AmfWrite(body, txn);
			if (args != null)
			{
				foreach (object v in args)
				{
					RtmpSession.AmfWrite(body, v);
				}
			}
			byte[] bytes = body.ToArray();
			SendMessage(csid, 20, sid, 0u, bytes, 0, bytes.Length);
		}

		// Envía un mensaje troceado con cabecera completa (formato 0) y el resto de trozos con formato 3.
		// Marcas de tiempo de 0xFFFFFF o más van en 4 bytes extra en cada trozo.
		private void SendMessage(int csid, byte type, uint sid, uint ts, byte[] payload, int off, int len)
		{
			bool extended = ts >= 16777215;
			uint ts24 = (extended ? 16777215u : ts);
			lock (writeLock)
			{
				if (raw == null)
				{
					throw new IOException("sin conexión");
				}
				MemoryStream ms = wbuf;
				ms.SetLength(0L);
				ms.WriteByte((byte)csid);
				ms.WriteByte((byte)(ts24 >> 16));
				ms.WriteByte((byte)(ts24 >> 8));
				ms.WriteByte((byte)ts24);
				ms.WriteByte((byte)(len >> 16));
				ms.WriteByte((byte)(len >> 8));
				ms.WriteByte((byte)len);
				ms.WriteByte(type);
				ms.WriteByte((byte)sid);
				ms.WriteByte((byte)(sid >> 8));
				ms.WriteByte((byte)(sid >> 16));
				ms.WriteByte((byte)(sid >> 24));
				if (extended)
				{
					WriteBE32(ms, ts);
				}
				int sent = 0;
				while (true)
				{
					int count = Math.Min(outChunk, len - sent);
					ms.Write(payload, off + sent, count);
					sent += count;
					if (sent >= len)
					{
						break;
					}
					ms.WriteByte((byte)(0xC0 | csid));
					if (extended)
					{
						WriteBE32(ms, ts);
					}
				}
				raw.Write(ms.GetBuffer(), 0, (int)ms.Length);
				// Si un mensaje enorme agrandó el búfer, se vuelve a dejar pequeño.
				if (ms.Capacity > 4194304)
				{
					ms.SetLength(0L);
					ms.Capacity = 65536;
				}
			}
		}

		private static void WriteBE32(Stream w, uint v)
		{
			w.WriteByte((byte)(v >> 24));
			w.WriteByte((byte)(v >> 16));
			w.WriteByte((byte)(v >> 8));
			w.WriteByte((byte)v);
		}

		// polite: avisa a la plataforma de que termina la emisión (FCUnpublish + deleteStream) antes de cerrar.
		public void Close(bool polite)
		{
			if (closed)
			{
				return;
			}
			if (polite && error == null && net != null)
			{
				try
				{
					tcp.SendTimeout = 2000;
					SendCommand(3, 0u, "FCUnpublish", 6.0, null, "");
					SendCommand(3, 0u, "deleteStream", 7.0, null, (double)streamId);
				}
				catch
				{
				}
			}
			Close();
		}

		public void Close()
		{
			closed = true;
			try
			{
				if (raw != null)
				{
					raw.Close();
				}
			}
			catch
			{
			}
			try
			{
				if (tcp != null)
				{
					tcp.Close();
				}
			}
			catch
			{
			}
			lock (sync)
			{
				Monitor.PulseAll(sync);
			}
		}

		private static uint BE32(byte[] b, int o)
		{
			return (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
		}

		private static byte[] BE32(uint v)
		{
			return new byte[4]
			{
				(byte)(v >> 24),
				(byte)(v >> 16),
				(byte)(v >> 8),
				(byte)v
			};
		}
	}
}
