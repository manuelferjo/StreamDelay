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

		public static bool SkipCertCheck;

		private TcpClient tcp;

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

		private string publishCode;

		private readonly Dictionary<double, List<object>> results = new Dictionary<double, List<object>>();

		private readonly Dictionary<int, InChunk> states = new Dictionary<int, InChunk>();

		private static readonly byte[] SetDataFrame = new byte[16]
		{
			2, 0, 13, 64, 115, 101, 116, 68, 97, 116,
			97, 70, 114, 97, 109, 101
		};

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

		public void Connect(string url, string name, int timeoutMs)
		{
			bool tls;
			string host;
			int port;
			string app;
			ParseUrl(url, out tls, out host, out port, out app);
			string value = (tls ? "rtmps://" : "rtmp://") + host + ":" + port + "/" + app;
			tcp = new TcpClient();
			tcp.NoDelay = true;
			tcp.SendBufferSize = 1048576;
			IAsyncResult asyncResult = tcp.BeginConnect(host, port, null, null);
			if (!asyncResult.AsyncWaitHandle.WaitOne(timeoutMs))
			{
				Close();
				throw new IOException("no responde " + host + ":" + port);
			}
			try
			{
				tcp.EndConnect(asyncResult);
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
				SslStream sslStream = new SslStream(stream, false, (object o, X509Certificate cert, X509Chain chain, SslPolicyErrors errs) => SkipCertCheck || errs == SslPolicyErrors.None);
				try
				{
					sslStream.AuthenticateAsClient(host, null, SslProtocols.Tls12, true);
				}
				catch (Exception ex2)
				{
					Close();
					throw new IOException("fallo de conexión segura (rtmps) con " + host + ": " + ex2.Message);
				}
				stream = sslStream;
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
			SendControl(1, BE32(4096u));
			outChunk = 4096;
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["app"] = app;
			dictionary["type"] = "nonprivate";
			dictionary["flashVer"] = "FMLE/3.0 (compatible; FMSc/1.0)";
			dictionary["swfUrl"] = value;
			dictionary["tcUrl"] = value;
			SendCommand(3, 0u, "connect", 1.0, dictionary);
			List<object> list = Wait(1.0, timeoutMs);
			if (list == null)
			{
				throw Fail("la plataforma no respondió al conectar");
			}
			if ((string)list[0] == "_error")
			{
				throw Fail("la plataforma rechazó la conexión (" + Describe(list) + ")");
			}
			SendCommand(3, 0u, "releaseStream", 2.0, null, name);
			SendCommand(3, 0u, "FCPublish", 3.0, null, name);
			object[] args = new object[1];
			SendCommand(3, 0u, "createStream", 4.0, args);
			list = Wait(4.0, timeoutMs);
			if (list == null || (string)list[0] != "_result")
			{
				throw Fail("la plataforma no abrió el canal de emisión");
			}
			streamId = 1u;
			for (int num = list.Count - 1; num >= 2; num--)
			{
				if (list[num] is double)
				{
					streamId = (uint)(double)list[num];
					break;
				}
			}
			SendCommand(4, streamId, "publish", 5.0, null, name, "live");
			lock (sync)
			{
				long num2 = Environment.TickCount + timeoutMs;
				while (publishCode == null && error == null && !closed)
				{
					int num3 = (int)(num2 - Environment.TickCount);
					if (num3 > 0)
					{
						Monitor.Wait(sync, num3);
						continue;
					}
					break;
				}
			}
			if (publishCode == null || publishCode.IndexOf("Publish.Start", StringComparison.OrdinalIgnoreCase) < 0)
			{
				string text = publishCode ?? error;
				throw Fail((text == null) ? "la plataforma no aceptó la emisión (¿clave incorrecta o caducada?)" : ("la plataforma rechazó la emisión: " + text));
			}
		}

		private IOException Fail(string m)
		{
			Close();
			return new IOException(m);
		}

		private static void ParseUrl(string url, out bool tls, out string host, out int port, out string app)
		{
			string text = url.Trim();
			int num = text.IndexOf("://", StringComparison.Ordinal);
			if (num < 0)
			{
				throw new IOException("servidor no válido: " + url);
			}
			string text2 = text.Substring(0, num).ToLowerInvariant();
			tls = text2 == "rtmps";
			if (text2 != "rtmp" && text2 != "rtmps")
			{
				throw new IOException("el servidor debe empezar por rtmp:// o rtmps://");
			}
			string text3 = text.Substring(num + 3);
			int num2 = text3.IndexOf('/');
			string text4 = ((num2 < 0) ? text3 : text3.Substring(0, num2));
			app = ((num2 < 0) ? "" : text3.Substring(num2 + 1).Trim('/'));
			port = (tls ? 443 : 1935);
			int num3 = text4.LastIndexOf(':');
			if (num3 > 0 && text4.IndexOf(']') < num3)
			{
				int result;
				if (int.TryParse(text4.Substring(num3 + 1), out result))
				{
					port = result;
				}
				text4 = text4.Substring(0, num3);
			}
			host = text4.Trim('[', ']');
		}

		private void Handshake()
		{
			byte[] array = new byte[1537];
			array[0] = 3;
			Random random = new Random();
			for (int i = 9; i < array.Length; i++)
			{
				array[i] = (byte)random.Next(256);
			}
			raw.Write(array, 0, array.Length);
			byte[] array2 = new byte[3073];
			ReadExact(array2, 0, array2.Length);
			if (array2[0] != 3)
			{
				throw Fail("respuesta no RTMP del servidor de la plataforma");
			}
			raw.Write(array2, 1, 1536);
		}

		private void ReadExact(byte[] b, int off, int n)
		{
			while (n > 0)
			{
				int num = net.Read(b, off, n);
				if (num <= 0)
				{
					throw new IOException("la plataforma cerró la conexión");
				}
				off += num;
				n -= num;
				bytesIn += num;
			}
		}

		private void ReadLoop()
		{
			try
			{
				while (!closed)
				{
					ReadExact(hdr, 0, 1);
					int num = hdr[0];
					int num2 = num >> 6;
					int num3 = num & 0x3F;
					switch (num3)
					{
					case 0:
						ReadExact(hdr, 0, 1);
						num3 = 64 + hdr[0];
						break;
					case 1:
						ReadExact(hdr, 0, 2);
						num3 = 64 + hdr[0] + hdr[1] * 256;
						break;
					}
					InChunk value;
					if (!states.TryGetValue(num3, out value))
					{
						value = new InChunk();
						states[num3] = value;
					}
					uint num4 = 0u;
					if (num2 <= 2)
					{
						ReadExact(hdr, 0, 3);
						num4 = (uint)((hdr[0] << 16) | (hdr[1] << 8) | hdr[2]);
					}
					if (num2 <= 1)
					{
						ReadExact(hdr, 0, 4);
						value.Len = (uint)((hdr[0] << 16) | (hdr[1] << 8) | hdr[2]);
						value.Type = hdr[3];
					}
					if (num2 == 0)
					{
						ReadExact(hdr, 0, 4);
					}
					if (num2 <= 2)
					{
						value.Ext = num4 == 16777215;
					}
					uint num5 = 0u;
					if (value.Ext)
					{
						ReadExact(hdr, 0, 4);
						num5 = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
					}
					if (num2 <= 2)
					{
						uint delta = (value.Ext ? num5 : num4);
						uint num6 = (value.Delta = delta);
						value.Ts = ((num2 == 0) ? num6 : (value.Ts + num6));
						value.Buf = null;
					}
					else if (value.Buf == null)
					{
						value.Ts += value.Delta;
					}
					if (value.Buf == null)
					{
						if (value.Len > 4194304)
						{
							throw new IOException("mensaje demasiado grande de la plataforma");
						}
						value.Buf = new byte[value.Len];
						value.Got = 0;
					}
					int num7 = Math.Min(inChunk, (int)value.Len - value.Got);
					if (num7 > 0)
					{
						ReadExact(value.Buf, value.Got, num7);
					}
					value.Got += num7;
					if (value.Got >= value.Len)
					{
						byte[] buf = value.Buf;
						value.Buf = null;
						Handle(value.Type, value.Ts, buf);
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
				if (msg.Length >= 4)
				{
					inChunk = Math.Max(1, (int)(BE32(msg, 0) & 0x7FFFFFFF));
				}
				break;
			case 4:
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
				if (msg.Length >= 4)
				{
					ackWindow = BE32(msg, 0);
				}
				break;
			case 6:
				SendControl(5, BE32(2500000u));
				break;
			case 20:
				Command(msg);
				break;
			case 17:
				if (msg.Length > 1)
				{
					byte[] array = new byte[msg.Length - 1];
					Buffer.BlockCopy(msg, 1, array, 0, array.Length);
					Command(array);
				}
				break;
			}
		}

		private void Command(byte[] msg)
		{
			AmfReader amfReader = new AmfReader(msg);
			List<object> list = new List<object>();
			try
			{
				while (amfReader.More)
				{
					list.Add(amfReader.Read());
				}
			}
			catch
			{
			}
			if (list.Count == 0)
			{
				return;
			}
			switch (list[0] as string)
			{
			case "_result":
			case "_error":
			{
				double key = ((list.Count > 1 && list[1] is double) ? ((double)list[1]) : 0.0);
				lock (sync)
				{
					results[key] = list;
					Monitor.PulseAll(sync);
					break;
				}
			}
			case "onStatus":
			{
				Dictionary<string, object> dictionary = null;
				for (int i = 2; i < list.Count; i++)
				{
					if (dictionary != null)
					{
						break;
					}
					dictionary = list[i] as Dictionary<string, object>;
				}
				if (dictionary == null)
				{
					break;
				}
				string text = Get(dictionary, "code");
				string text2 = Get(dictionary, "level");
				string text3 = Get(dictionary, "description");
				if (text.IndexOf("Publish.Start", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					lock (sync)
					{
						publishCode = text;
						Monitor.PulseAll(sync);
						break;
					}
				}
				if (!(text2 == "error") && text.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("BadName", StringComparison.OrdinalIgnoreCase) < 0)
				{
					break;
				}
				string text4 = text + ((text3.Length > 0) ? (" — " + text3) : "");
				lock (sync)
				{
					if (publishCode == null)
					{
						publishCode = text4;
					}
					else
					{
						error = text4;
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

		private static string Describe(List<object> r)
		{
			foreach (object item in r)
			{
				Dictionary<string, object> dictionary = item as Dictionary<string, object>;
				if (dictionary != null)
				{
					string text = Get(dictionary, "code");
					string text2 = Get(dictionary, "description");
					if (text.Length > 0 || text2.Length > 0)
					{
						return (text + " " + text2).Trim();
					}
				}
			}
			return "sin detalle";
		}

		private List<object> Wait(double txn, int timeoutMs)
		{
			lock (sync)
			{
				long num = Environment.TickCount + timeoutMs;
				List<object> value;
				while (!results.TryGetValue(txn, out value))
				{
					if (error != null || closed)
					{
						return null;
					}
					int num2 = (int)(num - Environment.TickCount);
					if (num2 <= 0)
					{
						return null;
					}
					Monitor.Wait(sync, num2);
				}
				return value;
			}
		}

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
				byte[] array = new byte[SetDataFrame.Length + data.Length];
				Buffer.BlockCopy(SetDataFrame, 0, array, 0, SetDataFrame.Length);
				Buffer.BlockCopy(data, 0, array, SetDataFrame.Length, data.Length);
				SendMessage(4, type, streamId, (uint)ts, array, 0, array.Length);
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
			MemoryStream memoryStream = new MemoryStream();
			RtmpSession.AmfWrite(memoryStream, name);
			RtmpSession.AmfWrite(memoryStream, txn);
			if (args != null)
			{
				foreach (object v in args)
				{
					RtmpSession.AmfWrite(memoryStream, v);
				}
			}
			byte[] array = memoryStream.ToArray();
			SendMessage(csid, 20, sid, 0u, array, 0, array.Length);
		}

		private void SendMessage(int csid, byte type, uint sid, uint ts, byte[] payload, int off, int len)
		{
			bool flag = ts >= 16777215;
			uint num = (flag ? 16777215u : ts);
			lock (writeLock)
			{
				if (raw == null)
				{
					throw new IOException("sin conexión");
				}
				MemoryStream memoryStream = wbuf;
				memoryStream.SetLength(0L);
				memoryStream.WriteByte((byte)csid);
				memoryStream.WriteByte((byte)(num >> 16));
				memoryStream.WriteByte((byte)(num >> 8));
				memoryStream.WriteByte((byte)num);
				memoryStream.WriteByte((byte)(len >> 16));
				memoryStream.WriteByte((byte)(len >> 8));
				memoryStream.WriteByte((byte)len);
				memoryStream.WriteByte(type);
				memoryStream.WriteByte((byte)sid);
				memoryStream.WriteByte((byte)(sid >> 8));
				memoryStream.WriteByte((byte)(sid >> 16));
				memoryStream.WriteByte((byte)(sid >> 24));
				if (flag)
				{
					WriteBE32(memoryStream, ts);
				}
				int num2 = 0;
				while (true)
				{
					int num3 = Math.Min(outChunk, len - num2);
					memoryStream.Write(payload, off + num2, num3);
					num2 += num3;
					if (num2 >= len)
					{
						break;
					}
					memoryStream.WriteByte((byte)(0xC0 | csid));
					if (flag)
					{
						WriteBE32(memoryStream, ts);
					}
				}
				raw.Write(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
				if (memoryStream.Capacity > 4194304)
				{
					memoryStream.SetLength(0L);
					memoryStream.Capacity = 65536;
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
