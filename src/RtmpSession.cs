// Una conexión de OBS al servidor RTMP local (lado servidor del protocolo RTMP).
//
// Hace el saludo inicial (handshake), lee los mensajes troceados (chunks), contesta a los
// comandos connect / createStream / publish y pasa al motor cada paquete de audio, vídeo y
// metadatos (server.Packet). Solo lo necesario para que OBS emita: no reproduce nada.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace StreamDelay
{
	internal class RtmpSession
	{
		// Estado de cada canal de chunks (chunk stream): RTMP repite solo lo que cambia de un trozo a otro.
		private class ChunkState
		{
			public uint Ts;

			public uint Delta;

			public uint Len;

			public uint StreamId;

			public byte Type;

			// La marca de tiempo va en 4 bytes extra (no cabía en 3).
			public bool Ext;

			// Mensaje a medio recibir y cuántos bytes lleva.
			public byte[] Buf;

			public int Got;
		}

		// Error de protocolo que se enseña en el registro (los de red no).
		private class RtmpProtocolException : Exception
		{
			public RtmpProtocolException(string m)
				: base(m)
			{
			}
		}

		// Tamaño máximo de un mensaje (16 MB).
		private const int MaxMessage = 16777216;

		private readonly RtmpServer server;

		private readonly TcpClient client;

		private readonly Stream net;

		private readonly BufferedStream input;

		private readonly object writeLock = new object();

		private readonly Dictionary<int, ChunkState> states = new Dictionary<int, ChunkState>();

		private readonly byte[] hdr = new byte[16];

		private int inChunkSize = 128;

		private int outChunkSize = 128;

		// Bytes recibidos y último acuse enviado (OBS pide un acuse cada ackWindow bytes).
		private long bytesIn;

		private long lastAck;

		private uint ackWindow;

		private bool publishing;

		public string Remote;

		public string App = "";

		// Lo que OBS manda como "clave" (nombre del stream).
		public string Name = "";

		// Claves del handshake "complejo" de Adobe (las conocen todos los clientes y servidores RTMP).
		private static readonly byte[] KeyTail = new byte[32]
		{
			240, 238, 194, 74, 128, 104, 190, 232, 46, 0,
			208, 209, 2, 158, 126, 87, 110, 236, 93, 45,
			41, 128, 111, 171, 147, 184, 230, 54, 207, 235,
			49, 174
		};

		private static readonly byte[] FmsKey = Concat(Encoding.ASCII.GetBytes("Genuine Adobe Flash Media Server 001"), KeyTail);

		private static readonly byte[] FpKey = Concat(Encoding.ASCII.GetBytes("Genuine Adobe Flash Player 001"), KeyTail);

		public RtmpSession(RtmpServer server, TcpClient client)
		{
			this.server = server;
			this.client = client;
			net = client.GetStream();
			input = new BufferedStream(net, 65536);
			try
			{
				Remote = client.Client.RemoteEndPoint.ToString();
			}
			catch
			{
				Remote = "?";
			}
		}

		public void Close()
		{
			try
			{
				client.Close();
			}
			catch
			{
			}
		}

		// Hilo de la conexión: saludo, lectura hasta que OBS se va y aviso de fin de emisión.
		public void Run()
		{
			try
			{
				Handshake();
				ReadLoop();
			}
			catch (Exception ex)
			{
				if (ex is RtmpProtocolException)
				{
					server.Log(ex.Message);
				}
			}
			finally
			{
				Close();
				if (publishing)
				{
					publishing = false;
					server.PublishEnd(this);
				}
			}
		}

		// ---------- Saludo inicial (handshake) ----------

		// C0 (versión 3) + C1 (1536 bytes) → S0 + S1 + S2 → C2.
		private void Handshake()
		{
			byte[] c0c1 = new byte[1537];
			if (!ReadN(c0c1, 0, 1))
			{
				throw new IOException("cerrado");
			}
			if (c0c1[0] == 22)
			{
				// 22 = inicio de TLS.
				throw new RtmpProtocolException("Se recibió una conexión cifrada (rtmps://). En OBS usa rtmp:// (sin s) para el servidor local.");
			}
			if (c0c1[0] != 3)
			{
				throw new RtmpProtocolException("Conexión desde " + Remote + " no es RTMP: ¿el servidor de OBS empieza por rtmp://?");
			}
			if (!ReadN(c0c1, 1, 1536))
			{
				throw new IOException("cerrado");
			}
			// S0 + S1 + S2: S1 aleatorio y S2 = eco de C1 (handshake simple) o firmado (complejo).
			byte[] reply = new byte[3073];
			reply[0] = 3;
			Random random = new Random();
			for (int i = 9; i < reply.Length; i++)
			{
				reply[i] = (byte)random.Next(256);
			}
			if (!ComplexHandshake(c0c1, reply))
			{
				Buffer.BlockCopy(c0c1, 1, reply, 1537, 1536);
			}
			lock (writeLock)
			{
				net.Write(reply, 0, reply.Length);
				net.Flush();
			}
			byte[] c2 = new byte[1536];
			if (!ReadN(c2, 0, 1536))
			{
				throw new IOException("cerrado");
			}
		}

		// Handshake "complejo" (con firmas HMAC-SHA256), el que usan OBS y Flash. false = el cliente
		// usa el simple. Si C1 lleva una firma válida, rellena S1 y S2 firmados en s.
		private static bool ComplexHandshake(byte[] c0c1, byte[] s)
		{
			byte[] c1 = new byte[1536];
			Buffer.BlockCopy(c0c1, 1, c1, 0, 1536);
			// Versión 0 = handshake simple.
			if (c1[4] == 0 && c1[5] == 0 && c1[6] == 0 && c1[7] == 0)
			{
				return false;
			}
			// La firma puede estar en dos sitios (esquemas 0 y 1).
			for (int scheme = 0; scheme < 2; scheme++)
			{
				int clientDigestAt = DigestOffset(c1, scheme);
				byte[] expected = Hmac(Sub(FpKey, 0, 30), WithoutDigest(c1, clientDigestAt));
				if (SameBytes(expected, 0, c1, clientDigestAt, 32))
				{
					byte[] clientDigest = new byte[32];
					Buffer.BlockCopy(c1, clientDigestAt, clientDigest, 0, 32);
					// S1: tiempo 0, versión 4.5.0.1 y nuestra firma.
					byte[] s1 = new byte[1536];
					Buffer.BlockCopy(s, 1, s1, 0, 1536);
					s1[0] = (s1[1] = (s1[2] = (s1[3] = 0)));
					s1[4] = 4;
					s1[5] = 5;
					s1[6] = 0;
					s1[7] = 1;
					int serverDigestAt = DigestOffset(s1, scheme);
					Buffer.BlockCopy(Hmac(Sub(FmsKey, 0, 36), WithoutDigest(s1, serverDigestAt)), 0, s1, serverDigestAt, 32);
					Buffer.BlockCopy(s1, 0, s, 1, 1536);
					// S2: aleatorio, firmado con una clave derivada de la firma del cliente.
					byte[] s2 = new byte[1536];
					Buffer.BlockCopy(s, 1537, s2, 0, 1536);
					byte[] key = Hmac(FmsKey, clientDigest);
					Buffer.BlockCopy(Hmac(key, Sub(s2, 0, 1504)), 0, s2, 1504, 32);
					Buffer.BlockCopy(s2, 0, s, 1537, 1536);
					return true;
				}
			}
			return false;
		}

		// Dónde está la firma de 32 bytes dentro del bloque de 1536.
		private static int DigestOffset(byte[] b, int scheme)
		{
			int at = ((scheme == 0) ? 8 : 772);
			return (b[at] + b[at + 1] + b[at + 2] + b[at + 3]) % 728 + ((scheme == 0) ? 12 : 776);
		}

		// El bloque sin los 32 bytes de la firma (lo que se firma).
		private static byte[] WithoutDigest(byte[] b, int off)
		{
			byte[] result = new byte[b.Length - 32];
			Buffer.BlockCopy(b, 0, result, 0, off);
			Buffer.BlockCopy(b, off + 32, result, off, b.Length - off - 32);
			return result;
		}

		private static byte[] Hmac(byte[] key, byte[] data)
		{
			using (HMACSHA256 hmac = new HMACSHA256(key))
			{
				return hmac.ComputeHash(data);
			}
		}

		private static bool SameBytes(byte[] a, int ao, byte[] b, int bo, int n)
		{
			for (int i = 0; i < n; i++)
			{
				if (a[ao + i] != b[bo + i])
				{
					return false;
				}
			}
			return true;
		}

		private static byte[] Concat(byte[] a, byte[] b)
		{
			byte[] result = new byte[a.Length + b.Length];
			Buffer.BlockCopy(a, 0, result, 0, a.Length);
			Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
			return result;
		}

		private static byte[] Sub(byte[] b, int from, int len)
		{
			byte[] result = new byte[len];
			Buffer.BlockCopy(b, from, result, 0, len);
			return result;
		}

		// ---------- Lectura de mensajes ----------

		// Lee trozos (chunks) y junta cada mensaje. Cabecera de cada trozo: formato (2 bits) y canal,
		// y según el formato: marca de tiempo (3), longitud y tipo (4), id de stream (4) y marca extendida (4).
		private void ReadLoop()
		{
			while (true)
			{
				int first = ReadByte();
				if (first < 0)
				{
					break;
				}
				int format = first >> 6;
				int csid = first & 0x3F;
				// Canales 0 y 1: el número va en 1 o 2 bytes más.
				switch (csid)
				{
				case 0:
					csid = 64 + ReadByteOrThrow();
					break;
				case 1:
				{
					int low = ReadByteOrThrow();
					int high = ReadByteOrThrow();
					csid = 64 + low + high * 256;
					break;
				}
				}
				ChunkState st;
				if (!states.TryGetValue(csid, out st))
				{
					st = new ChunkState();
					states[csid] = st;
				}
				uint ts24 = 0u;
				if (format <= 2)
				{
					ReadOrThrow(hdr, 3);
					ts24 = U24(hdr, 0);
				}
				if (format <= 1)
				{
					ReadOrThrow(hdr, 4);
					st.Len = U24(hdr, 0);
					st.Type = hdr[3];
				}
				if (format == 0)
				{
					// El id de stream va al revés (little endian).
					ReadOrThrow(hdr, 4);
					st.StreamId = (uint)(hdr[0] | (hdr[1] << 8) | (hdr[2] << 16) | (hdr[3] << 24));
				}
				if (format <= 2)
				{
					st.Ext = ts24 == 16777215;
				}
				uint extTs = 0u;
				if (st.Ext)
				{
					ReadOrThrow(hdr, 4);
					extTs = BE32(hdr, 0);
				}
				if (format <= 2)
				{
					// Formato 0: marca absoluta; 1 y 2: incremento sobre la anterior.
					uint delta = (st.Ext ? extTs : ts24);
					uint step = (st.Delta = delta);
					st.Ts = ((format == 0) ? step : (st.Ts + step));
					st.Buf = null;
				}
				else if (st.Buf == null)
				{
					// Formato 3 al empezar un mensaje nuevo: mismo incremento que el anterior.
					st.Ts += st.Delta;
				}
				if (st.Buf == null)
				{
					if (st.Len > MaxMessage)
					{
						throw new RtmpProtocolException("Mensaje RTMP demasiado grande (" + st.Len + " bytes).");
					}
					st.Buf = new byte[st.Len];
					st.Got = 0;
				}
				int count = Math.Min(inChunkSize, (int)st.Len - st.Got);
				if (count > 0 && !ReadN(st.Buf, st.Got, count))
				{
					break;
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
					SendControl(3, BE32Bytes((uint)bytesIn));
				}
			}
		}

		// Un mensaje completo, según su tipo RTMP.
		private void Handle(byte type, uint ts, byte[] msg)
		{
			switch (type)
			{
			case 1:
				// Tamaño de trozo.
				if (msg.Length >= 4)
				{
					inChunkSize = Math.Max(1, (int)(BE32(msg, 0) & 0x7FFFFFFF));
				}
				break;
			case 5:
				// Ventana de acuses.
				if (msg.Length >= 4)
				{
					ackWindow = BE32(msg, 0);
				}
				break;
			case 8:
			case 9:
				// Audio y vídeo.
				if (publishing && msg.Length > 0)
				{
					server.Packet(this, type, ts, msg);
				}
				break;
			case 15:
				// Datos AMF3 (un byte de más delante).
				if (msg.Length > 1)
				{
					Data(Sub(msg, 1), ts);
				}
				break;
			case 18:
				Data(msg, ts);
				break;
			case 17:
				// Comando AMF3.
				if (msg.Length > 1)
				{
					Command(Sub(msg, 1));
				}
				break;
			case 20:
				Command(msg);
				break;
			}
		}

		// Metadatos (onMetaData): se les quita el "@setDataFrame" que añade OBS y van al motor.
		private void Data(byte[] msg, uint ts)
		{
			if (publishing)
			{
				if (msg.Length > 16 && msg[0] == 2 && msg[1] == 0 && msg[2] == 13 && Encoding.ASCII.GetString(msg, 3, 13) == "@setDataFrame")
				{
					msg = Sub(msg, 16);
				}
				server.Packet(this, 18, ts, msg);
			}
		}

		// Comandos de OBS: connect → createStream → publish (… y al terminar FCUnpublish/deleteStream).
		private void Command(byte[] msg)
		{
			AmfReader reader = new AmfReader(msg);
			string name = reader.Read() as string;
			object txnValue = (reader.More ? reader.Read() : null);
			double txn = ((txnValue is double) ? ((double)txnValue) : 0.0);
			switch (name)
			{
			case "connect":
			{
				Dictionary<string, object> props = (reader.More ? (reader.Read() as Dictionary<string, object>) : null);
				object value;
				if (props != null && props.TryGetValue("app", out value) && value is string)
				{
					App = (string)value;
				}
				// Ventana de acuses, ancho de banda y trozos de 4096 bytes.
				SendControl(5, BE32Bytes(2500000u));
				SendControl(6, new byte[5] { 0, 38, 37, 160, 2 });
				SendControl(1, BE32Bytes(4096u));
				outChunkSize = 4096;
				SendCommand(3, 0u, "_result", txn, Obj("fmsVer", "FMS/3,0,1,123", "capabilities", 31.0), Obj("level", "status", "code", "NetConnection.Connect.Success", "description", "Connection succeeded.", "objectEncoding", 0.0));
				break;
			}
			case "releaseStream":
			case "FCPublish":
			{
				// Respuesta vacía (null, null).
				object[] args = new object[2];
				SendCommand(3, 0u, "_result", txn, args);
				break;
			}
			case "createStream":
				SendCommand(3, 0u, "_result", txn, null, 1.0);
				break;
			case "publish":
				// publish(null, nombre, tipo): el nombre es la clave de OBS.
				if (reader.More)
				{
					reader.Read();
				}
				Name = (reader.More ? ((reader.Read() as string) ?? "") : "");
				// Stream Begin (stream 1).
				SendControl(4, new byte[6] { 0, 0, 0, 0, 0, 1 });
				SendCommand(5, 1u, "onStatus", 0.0, null, Obj("level", "status", "code", "NetStream.Publish.Start", "description", "Publishing " + Name + ".", "details", Name));
				if (!publishing)
				{
					publishing = true;
					server.PublishStart(this);
				}
				break;
			case "FCUnpublish":
			case "deleteStream":
			case "closeStream":
				if (publishing)
				{
					publishing = false;
					server.PublishEnd(this);
				}
				break;
			}
		}

		// ---------- Envío ----------

		// Mensaje de control (canal 2, stream 0).
		private void SendControl(byte type, byte[] payload)
		{
			SendMessage(2, type, 0u, payload);
		}

		// Comando AMF0: nombre, número de transacción y argumentos.
		private void SendCommand(int csid, uint streamId, string name, double txn, params object[] args)
		{
			MemoryStream body = new MemoryStream();
			AmfWrite(body, name);
			AmfWrite(body, txn);
			foreach (object v in args)
			{
				AmfWrite(body, v);
			}
			SendMessage(csid, 20, streamId, body.ToArray());
		}

		// Envía un mensaje troceado: cabecera completa (formato 0) y el resto de trozos con formato 3.
		private void SendMessage(int csid, byte type, uint streamId, byte[] payload)
		{
			MemoryStream ms = new MemoryStream();
			ms.WriteByte((byte)csid);
			// Marca de tiempo 0.
			byte[] zeroTs = new byte[3];
			ms.Write(zeroTs, 0, 3);
			int length = payload.Length;
			ms.Write(new byte[4]
			{
				(byte)(length >> 16),
				(byte)(length >> 8),
				(byte)length,
				type
			}, 0, 4);
			ms.Write(new byte[4]
			{
				(byte)streamId,
				(byte)(streamId >> 8),
				(byte)(streamId >> 16),
				(byte)(streamId >> 24)
			}, 0, 4);
			for (int i = 0; i < length; i += outChunkSize)
			{
				if (i > 0)
				{
					ms.WriteByte((byte)(0xC0 | csid));
				}
				ms.Write(payload, i, Math.Min(outChunkSize, length - i));
			}
			byte[] bytes = ms.ToArray();
			lock (writeLock)
			{
				net.Write(bytes, 0, bytes.Length);
				net.Flush();
			}
		}

		// Objeto AMF a partir de pares clave, valor.
		private static Dictionary<string, object> Obj(params object[] kv)
		{
			Dictionary<string, object> obj = new Dictionary<string, object>();
			for (int i = 0; i + 1 < kv.Length; i += 2)
			{
				obj[(string)kv[i]] = kv[i + 1];
			}
			return obj;
		}

		// Escribe un valor en AMF0: null (5), texto (2), número (0), sí/no (1) u objeto (3).
		internal static void AmfWrite(Stream s, object v)
		{
			if (v == null)
			{
				s.WriteByte(5);
				return;
			}
			if (v is string)
			{
				byte[] text = Encoding.UTF8.GetBytes((string)v);
				s.WriteByte(2);
				s.WriteByte((byte)(text.Length >> 8));
				s.WriteByte((byte)text.Length);
				s.Write(text, 0, text.Length);
				return;
			}
			if (v is double)
			{
				// Número de 8 bytes, al revés (big endian).
				byte[] number = BitConverter.GetBytes((double)v);
				if (BitConverter.IsLittleEndian)
				{
					Array.Reverse(number);
				}
				s.WriteByte(0);
				s.Write(number, 0, 8);
				return;
			}
			if (v is bool)
			{
				s.WriteByte(1);
				s.WriteByte((byte)(((bool)v) ? 1u : 0u));
				return;
			}
			Dictionary<string, object> obj = v as Dictionary<string, object>;
			if (obj != null)
			{
				s.WriteByte(3);
				foreach (KeyValuePair<string, object> item in obj)
				{
					byte[] key = Encoding.UTF8.GetBytes(item.Key);
					s.WriteByte((byte)(key.Length >> 8));
					s.WriteByte((byte)key.Length);
					s.Write(key, 0, key.Length);
					AmfWrite(s, item.Value);
				}
				// Fin de objeto: 00 00 09.
				s.WriteByte(0);
				s.WriteByte(0);
				s.WriteByte(9);
			}
			else
			{
				// Cualquier otra cosa: undefined.
				s.WriteByte(6);
			}
		}

		// ---------- Lectura de bytes ----------

		// Lee exactamente n bytes. false = conexión cerrada.
		private bool ReadN(byte[] buf, int off, int n)
		{
			int got;
			for (int i = 0; i < n; i += got)
			{
				got = input.Read(buf, off + i, n - i);
				if (got <= 0)
				{
					return false;
				}
			}
			bytesIn += n;
			return true;
		}

		private void ReadOrThrow(byte[] buf, int n)
		{
			if (!ReadN(buf, 0, n))
			{
				throw new IOException("cerrado");
			}
		}

		private int ReadByte()
		{
			int b = input.ReadByte();
			if (b >= 0)
			{
				bytesIn++;
			}
			return b;
		}

		private int ReadByteOrThrow()
		{
			int b = ReadByte();
			if (b < 0)
			{
				throw new IOException("cerrado");
			}
			return b;
		}

		// Enteros big endian de 3 y 4 bytes.
		private static uint U24(byte[] b, int o)
		{
			return (uint)((b[o] << 16) | (b[o + 1] << 8) | b[o + 2]);
		}

		private static uint BE32(byte[] b, int o)
		{
			return (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
		}

		private static byte[] BE32Bytes(uint v)
		{
			return new byte[4]
			{
				(byte)(v >> 24),
				(byte)(v >> 16),
				(byte)(v >> 8),
				(byte)v
			};
		}

		private static byte[] Sub(byte[] b, int from)
		{
			byte[] result = new byte[b.Length - from];
			Buffer.BlockCopy(b, from, result, 0, result.Length);
			return result;
		}
	}
}
