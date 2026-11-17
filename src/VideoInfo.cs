using System;
using System.Collections.Generic;

namespace StreamDelay
{
	internal static class VideoInfo
	{
		private class Bits
		{
			private readonly byte[] b;

			private int pos;

			public Bits(byte[] b)
			{
				this.b = b;
			}

			public int U(int n)
			{
				int num = 0;
				for (int i = 0; i < n; i++)
				{
					if (pos >= b.Length * 8)
					{
						throw new IndexOutOfRangeException();
					}
					num = (num << 1) | ((b[pos >> 3] >> 7 - (pos & 7)) & 1);
					pos++;
				}
				return num;
			}

			public int Ue()
			{
				int num = 0;
				while (U(1) == 0)
				{
					if (++num > 31)
					{
						throw new FormatException();
					}
				}
				return (1 << num) - 1 + U(num);
			}

			public int Se()
			{
				int num = Ue();
				if ((num & 1) != 1)
				{
					return -(num / 2);
				}
				return (num + 1) / 2;
			}
		}

		public static int Height(FlvPacket pk)
		{
			try
			{
				byte[] data = pk.Data;
				int num;
				if ((data[0] & 0x80) == 0)
				{
					if ((data[0] & 0xF) != 7)
					{
						return 0;
					}
					num = 5;
				}
				else
				{
					bool flag = (data[0] & 0xF) == 6;
					int num2 = ((!flag) ? 1 : 2);
					if (data[num2] != 97 || data[num2 + 1] != 118 || data[num2 + 2] != 99 || data[num2 + 3] != 49)
					{
						return 0;
					}
					num = (flag ? 7 : 5);
				}
				if (data.Length < num + 8 || data[num] != 1 || (data[num + 5] & 0x1F) == 0)
				{
					return 0;
				}
				int num3 = (data[num + 6] << 8) | data[num + 7];
				if (data.Length < num + 8 + num3 || num3 < 4)
				{
					return 0;
				}
				return SpsHeight(data, num + 8, num3);
			}
			catch
			{
				return 0;
			}
		}

		private static int SpsHeight(byte[] b, int start, int len)
		{
			List<byte> list = new List<byte>(len);
			for (int i = start + 1; i < start + len; i++)
			{
				if (i + 2 < start + len && b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 3)
				{
					list.Add(0);
					list.Add(0);
					i += 2;
				}
				else
				{
					list.Add(b[i]);
				}
			}
			Bits bits = new Bits(list.ToArray());
			int num = bits.U(8);
			bits.U(8);
			bits.U(8);
			bits.Ue();
			int num2 = 1;
			if (num == 100 || num == 110 || num == 122 || num == 244 || num == 44 || num == 83 || num == 86 || num == 118 || num == 128 || num == 138 || num == 139 || num == 134 || num == 135)
			{
				num2 = bits.Ue();
				if (num2 == 3)
				{
					bits.U(1);
				}
				bits.Ue();
				bits.Ue();
				bits.U(1);
				if (bits.U(1) == 1)
				{
					for (int j = 0; j < ((num2 != 3) ? 8 : 12); j++)
					{
						if (bits.U(1) != 1)
						{
							continue;
						}
						int num3 = ((j < 6) ? 16 : 64);
						int num4 = 8;
						int num5 = 8;
						for (int k = 0; k < num3; k++)
						{
							if (num5 != 0)
							{
								num5 = (num4 + bits.Se() + 256) % 256;
							}
							num4 = ((num5 == 0) ? num4 : num5);
						}
					}
				}
			}
			bits.Ue();
			switch (bits.Ue())
			{
			case 0:
				bits.Ue();
				break;
			case 1:
			{
				bits.U(1);
				bits.Se();
				bits.Se();
				int num6 = bits.Ue();
				for (int l = 0; l < num6; l++)
				{
					bits.Se();
				}
				break;
			}
			}
			bits.Ue();
			bits.U(1);
			bits.Ue();
			int num7 = bits.Ue();
			int num8 = bits.U(1);
			if (num8 == 0)
			{
				bits.U(1);
			}
			bits.U(1);
			int num9 = 0;
			int num10 = 0;
			if (bits.U(1) == 1)
			{
				bits.Ue();
				bits.Ue();
				num9 = bits.Ue();
				num10 = bits.Ue();
			}
			int num11 = ((num2 != 1) ? 1 : 2) * (2 - num8);
			if (num2 == 0)
			{
				num11 = 2 - num8;
			}
			return (2 - num8) * (num7 + 1) * 16 - (num9 + num10) * num11;
		}

		// Convierte un paquete de vídeo multipista (RTMP mejorado) al formato de una sola pista que entiende
		// cualquier plataforma. H.264 pasa al formato FLV clásico; otros códecs, a RTMP mejorado sin multipista.
		public static byte[] ToSingleTrack(byte[] d)
		{
			if (d.Length < 2 || (d[0] & 0x80) == 0)
			{
				return d;
			}
			int ft = (d[0] >> 4) & 7;
			int pt = d[0] & 0x0F;
			int off;
			int fc;
			if (pt == 6)
			{
				if (d.Length < 7 || (d[1] & 0xF0) != 0)
				{
					return null;
				}
				pt = d[1] & 0x0F;
				fc = 2;
				off = 7;
			}
			else
			{
				fc = 1;
				off = 5;
			}
			if (d.Length < off)
			{
				return null;
			}
			bool avc = d[fc] == 'a' && d[fc + 1] == 'v' && d[fc + 2] == 'c' && d[fc + 3] == '1';
			if (!avc)
			{
				if (fc == 1)
				{
					return d;
				}
				byte[] x = new byte[d.Length - 2];
				x[0] = (byte)(0x80 | (ft << 4) | pt);
				Buffer.BlockCopy(d, 2, x, 1, 4);
				Buffer.BlockCopy(d, 7, x, 5, d.Length - 7);
				return x;
			}
			int avcType;
			int cts = 0;
			if (pt == 0)
			{
				avcType = 0;
			}
			else if (pt == 1)
			{
				if (d.Length < off + 3)
				{
					return null;
				}
				avcType = 1;
				cts = 3;
			}
			else if (pt == 3)
			{
				avcType = 1;
			}
			else if (pt == 2)
			{
				avcType = 2;
			}
			else
			{
				return null;
			}
			byte[] o = new byte[5 + d.Length - off - cts];
			o[0] = (byte)((ft << 4) | 7);
			o[1] = (byte)avcType;
			if (cts == 3)
			{
				o[2] = d[off];
				o[3] = d[off + 1];
				o[4] = d[off + 2];
			}
			Buffer.BlockCopy(d, off + cts, o, 5, d.Length - off - cts);
			return o;
		}

		public static string Label(int h, int fps)
		{
			return h + "p" + ((fps >= 50) ? fps.ToString() : "");
		}
	}
}
