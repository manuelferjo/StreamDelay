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
		private class ChunkState
		{
			public uint Ts;

			public uint Delta;

			public uint Len;

			public uint StreamId;

			public byte Type;

			public bool Ext;

			public byte[] Buf;

			public int Got;
		}

		private class RtmpProtocolException : Exception
		{
			public RtmpProtocolException(string m)
				: base(m)
			{
			}
		}

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

		private long bytesIn;

		private long lastAck;

		private uint ackWindow;

		private bool publishing;

		public string Remote;

		public string App = "";

		public string Name = "";

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

		private void Handshake()
		{
			byte[] array = new byte[1537];
			if (!ReadN(array, 0, 1))
			{
				throw new IOException("cerrado");
			}
			if (array[0] == 22)
			{
				throw new RtmpProtocolException("Se recibió una conexión cifrada (rtmps://). En OBS usa rtmp:// (sin s) para el servidor local.");
			}
			if (array[0] != 3)
			{
				throw new RtmpProtocolException("Conexión desde " + Remote + " no es RTMP: ¿el servidor de OBS empieza por rtmp://?");
			}
			if (!ReadN(array, 1, 1536))
			{
				throw new IOException("cerrado");
			}
			byte[] array2 = new byte[3073];
			array2[0] = 3;
			Random random = new Random();
			for (int i = 9; i < array2.Length; i++)
			{
				array2[i] = (byte)random.Next(256);
			}
			if (!ComplexHandshake(array, array2))
			{
				Buffer.BlockCopy(array, 1, array2, 1537, 1536);
			}
			lock (writeLock)
			{
				net.Write(array2, 0, array2.Length);
				net.Flush();
			}
			byte[] buf = new byte[1536];
			if (!ReadN(buf, 0, 1536))
			{
				throw new IOException("cerrado");
			}
		}

		private static bool ComplexHandshake(byte[] c0c1, byte[] s)
		{
			byte[] array = new byte[1536];
			Buffer.BlockCopy(c0c1, 1, array, 0, 1536);
			if (array[4] == 0 && array[5] == 0 && array[6] == 0 && array[7] == 0)
			{
				return false;
			}
			for (int i = 0; i < 2; i++)
			{
				int num = DigestOffset(array, i);
				byte[] a = Hmac(Sub(FpKey, 0, 30), WithoutDigest(array, num));
				if (SameBytes(a, 0, array, num, 32))
				{
					byte[] array2 = new byte[32];
					Buffer.BlockCopy(array, num, array2, 0, 32);
					byte[] array3 = new byte[1536];
					Buffer.BlockCopy(s, 1, array3, 0, 1536);
					array3[0] = (array3[1] = (array3[2] = (array3[3] = 0)));
					array3[4] = 4;
					array3[5] = 5;
					array3[6] = 0;
					array3[7] = 1;
					int num2 = DigestOffset(array3, i);
					Buffer.BlockCopy(Hmac(Sub(FmsKey, 0, 36), WithoutDigest(array3, num2)), 0, array3, num2, 32);
					Buffer.BlockCopy(array3, 0, s, 1, 1536);
					byte[] array4 = new byte[1536];
					Buffer.BlockCopy(s, 1537, array4, 0, 1536);
					byte[] key = Hmac(FmsKey, array2);
					Buffer.BlockCopy(Hmac(key, Sub(array4, 0, 1504)), 0, array4, 1504, 32);
					Buffer.BlockCopy(array4, 0, s, 1537, 1536);
					return true;
				}
			}
			return false;
		}

		private static int DigestOffset(byte[] b, int scheme)
		{
			int num = ((scheme == 0) ? 8 : 772);
			return (b[num] + b[num + 1] + b[num + 2] + b[num + 3]) % 728 + ((scheme == 0) ? 12 : 776);
		}

		private static byte[] WithoutDigest(byte[] b, int off)
		{
			byte[] array = new byte[b.Length - 32];
			Buffer.BlockCopy(b, 0, array, 0, off);
			Buffer.BlockCopy(b, off + 32, array, off, b.Length - off - 32);
			return array;
		}

		private static byte[] Hmac(byte[] key, byte[] data)
		{
			using (HMACSHA256 hMACSHA = new HMACSHA256(key))
			{
				return hMACSHA.ComputeHash(data);
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
			byte[] array = new byte[a.Length + b.Length];
			Buffer.BlockCopy(a, 0, array, 0, a.Length);
			Buffer.BlockCopy(b, 0, array, a.Length, b.Length);
			return array;
		}

		private static byte[] Sub(byte[] b, int from, int len)
		{
			byte[] array = new byte[len];
			Buffer.BlockCopy(b, from, array, 0, len);
			return array;
		}

		private void ReadLoop()
		{
			while (true)
			{
				int num = ReadByte();
				if (num < 0)
				{
					break;
				}
				int num2 = num >> 6;
				int num3 = num & 0x3F;
				switch (num3)
				{
				case 0:
					num3 = 64 + ReadByteOrThrow();
					break;
				case 1:
				{
					int num4 = ReadByteOrThrow();
					int num5 = ReadByteOrThrow();
					num3 = 64 + num4 + num5 * 256;
					break;
				}
				}
				ChunkState value;
				if (!states.TryGetValue(num3, out value))
				{
					value = new ChunkState();
					states[num3] = value;
				}
				uint num6 = 0u;
				if (num2 <= 2)
				{
					ReadOrThrow(hdr, 3);
					num6 = U24(hdr, 0);
				}
				if (num2 <= 1)
				{
					ReadOrThrow(hdr, 4);
					value.Len = U24(hdr, 0);
					value.Type = hdr[3];
				}
				if (num2 == 0)
				{
					ReadOrThrow(hdr, 4);
					value.StreamId = (uint)(hdr[0] | (hdr[1] << 8) | (hdr[2] << 16) | (hdr[3] << 24));
				}
				if (num2 <= 2)
				{
					value.Ext = num6 == 16777215;
				}
				uint num7 = 0u;
				if (value.Ext)
				{
					ReadOrThrow(hdr, 4);
					num7 = BE32(hdr, 0);
				}
				if (num2 <= 2)
				{
					uint delta = (value.Ext ? num7 : num6);
					uint num8 = (value.Delta = delta);
					value.Ts = ((num2 == 0) ? num8 : (value.Ts + num8));
					value.Buf = null;
				}
				else if (value.Buf == null)
				{
					value.Ts += value.Delta;
				}
				if (value.Buf == null)
				{
					if (value.Len > 16777216)
					{
						throw new RtmpProtocolException("Mensaje RTMP demasiado grande (" + value.Len + " bytes).");
					}
					value.Buf = new byte[value.Len];
					value.Got = 0;
				}
				int num9 = Math.Min(inChunkSize, (int)value.Len - value.Got);
				if (num9 > 0 && !ReadN(value.Buf, value.Got, num9))
				{
					break;
				}
				value.Got += num9;
				if (value.Got >= value.Len)
				{
					byte[] buf = value.Buf;
					value.Buf = null;
					Handle(value.Type, value.Ts, buf);
				}
				if (ackWindow != 0 && bytesIn - lastAck >= ackWindow)
				{
					lastAck = bytesIn;
					SendControl(3, BE32Bytes((uint)bytesIn));
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
					inChunkSize = Math.Max(1, (int)(BE32(msg, 0) & 0x7FFFFFFF));
				}
				break;
			case 5:
				if (msg.Length >= 4)
				{
					ackWindow = BE32(msg, 0);
				}
				break;
			case 8:
			case 9:
				if (publishing && msg.Length > 0)
				{
					server.Packet(this, type, ts, msg);
				}
				break;
			case 15:
				if (msg.Length > 1)
				{
					Data(Sub(msg, 1), ts);
				}
				break;
			case 18:
				Data(msg, ts);
				break;
			case 17:
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

		private void Command(byte[] msg)
		{
			AmfReader amfReader = new AmfReader(msg);
			string text = amfReader.Read() as string;
			object obj = (amfReader.More ? amfReader.Read() : null);
			double txn = ((obj is double) ? ((double)obj) : 0.0);
			switch (text)
			{
			case "connect":
			{
				Dictionary<string, object> dictionary = (amfReader.More ? (amfReader.Read() as Dictionary<string, object>) : null);
				object value;
				if (dictionary != null && dictionary.TryGetValue("app", out value) && value is string)
				{
					App = (string)value;
				}
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
				object[] args = new object[2];
				SendCommand(3, 0u, "_result", txn, args);
				break;
			}
			case "createStream":
				SendCommand(3, 0u, "_result", txn, null, 1.0);
				break;
			case "publish":
				if (amfReader.More)
				{
					amfReader.Read();
				}
				Name = (amfReader.More ? ((amfReader.Read() as string) ?? "") : "");
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

		private void SendControl(byte type, byte[] payload)
		{
			SendMessage(2, type, 0u, payload);
		}

		private void SendCommand(int csid, uint streamId, string name, double txn, params object[] args)
		{
			MemoryStream memoryStream = new MemoryStream();
			AmfWrite(memoryStream, name);
			AmfWrite(memoryStream, txn);
			foreach (object v in args)
			{
				AmfWrite(memoryStream, v);
			}
			SendMessage(csid, 20, streamId, memoryStream.ToArray());
		}

		private void SendMessage(int csid, byte type, uint streamId, byte[] payload)
		{
			MemoryStream memoryStream = new MemoryStream();
			memoryStream.WriteByte((byte)csid);
			byte[] buffer = new byte[3];
			memoryStream.Write(buffer, 0, 3);
			int num = payload.Length;
			memoryStream.Write(new byte[4]
			{
				(byte)(num >> 16),
				(byte)(num >> 8),
				(byte)num,
				type
			}, 0, 4);
			memoryStream.Write(new byte[4]
			{
				(byte)streamId,
				(byte)(streamId >> 8),
				(byte)(streamId >> 16),
				(byte)(streamId >> 24)
			}, 0, 4);
			for (int i = 0; i < num; i += outChunkSize)
			{
				if (i > 0)
				{
					memoryStream.WriteByte((byte)(0xC0 | csid));
				}
				memoryStream.Write(payload, i, Math.Min(outChunkSize, num - i));
			}
			byte[] array = memoryStream.ToArray();
			lock (writeLock)
			{
				net.Write(array, 0, array.Length);
				net.Flush();
			}
		}

		private static Dictionary<string, object> Obj(params object[] kv)
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			for (int i = 0; i + 1 < kv.Length; i += 2)
			{
				dictionary[(string)kv[i]] = kv[i + 1];
			}
			return dictionary;
		}

		internal static void AmfWrite(Stream s, object v)
		{
			if (v == null)
			{
				s.WriteByte(5);
				return;
			}
			if (v is string)
			{
				byte[] bytes = Encoding.UTF8.GetBytes((string)v);
				s.WriteByte(2);
				s.WriteByte((byte)(bytes.Length >> 8));
				s.WriteByte((byte)bytes.Length);
				s.Write(bytes, 0, bytes.Length);
				return;
			}
			if (v is double)
			{
				byte[] bytes2 = BitConverter.GetBytes((double)v);
				if (BitConverter.IsLittleEndian)
				{
					Array.Reverse(bytes2);
				}
				s.WriteByte(0);
				s.Write(bytes2, 0, 8);
				return;
			}
			if (v is bool)
			{
				s.WriteByte(1);
				s.WriteByte((byte)(((bool)v) ? 1u : 0u));
				return;
			}
			Dictionary<string, object> dictionary = v as Dictionary<string, object>;
			if (dictionary != null)
			{
				s.WriteByte(3);
				foreach (KeyValuePair<string, object> item in dictionary)
				{
					byte[] bytes3 = Encoding.UTF8.GetBytes(item.Key);
					s.WriteByte((byte)(bytes3.Length >> 8));
					s.WriteByte((byte)bytes3.Length);
					s.Write(bytes3, 0, bytes3.Length);
					AmfWrite(s, item.Value);
				}
				s.WriteByte(0);
				s.WriteByte(0);
				s.WriteByte(9);
			}
			else
			{
				s.WriteByte(6);
			}
		}

		private bool ReadN(byte[] buf, int off, int n)
		{
			int num;
			for (int i = 0; i < n; i += num)
			{
				num = input.Read(buf, off + i, n - i);
				if (num <= 0)
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
			int num = input.ReadByte();
			if (num >= 0)
			{
				bytesIn++;
			}
			return num;
		}

		private int ReadByteOrThrow()
		{
			int num = ReadByte();
			if (num < 0)
			{
				throw new IOException("cerrado");
			}
			return num;
		}

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
			byte[] array = new byte[b.Length - from];
			Buffer.BlockCopy(b, from, array, 0, array.Length);
			return array;
		}
	}
}
