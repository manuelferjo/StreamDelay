// Datos del vídeo que hacen falta sin decodificarlo: la altura (720, 1080…) leída de la cabecera
// H.264 (SPS), el paso de multipista a una sola pista y el texto "1080p60".
using System;
using System.Collections.Generic;

namespace StreamDelay
{
	internal static class VideoInfo
	{
		// Lector de bits (con los números Exp-Golomb de H.264).
		private class Bits
		{
			private readonly byte[] b;

			private int pos;

			public Bits(byte[] b)
			{
				this.b = b;
			}

			// n bits sin signo.
			public int U(int n)
			{
				int value = 0;
				for (int i = 0; i < n; i++)
				{
					if (pos >= b.Length * 8)
					{
						throw new IndexOutOfRangeException();
					}
					value = (value << 1) | ((b[pos >> 3] >> 7 - (pos & 7)) & 1);
					pos++;
				}
				return value;
			}

			// Exp-Golomb sin signo: N ceros, un 1 y N bits.
			public int Ue()
			{
				int zeros = 0;
				while (U(1) == 0)
				{
					if (++zeros > 31)
					{
						throw new FormatException();
					}
				}
				return (1 << zeros) - 1 + U(zeros);
			}

			// Exp-Golomb con signo.
			public int Se()
			{
				int code = Ue();
				if ((code & 1) != 1)
				{
					return -(code / 2);
				}
				return (code + 1) / 2;
			}
		}

		// Altura del vídeo a partir de la cabecera de secuencia H.264 (0 si no es H.264 o no se puede leer).
		public static int Height(FlvPacket pk)
		{
			try
			{
				byte[] data = pk.Data;
				// Dónde empieza la configuración AVC (AVCDecoderConfigurationRecord).
				int record;
				if ((data[0] & 0x80) == 0)
				{
					// FLV clásico: códec 7 = H.264; 1 byte de cabecera, tipo de paquete y 3 de tiempo.
					if ((data[0] & 0xF) != 7)
					{
						return 0;
					}
					record = 5;
				}
				else
				{
					// Enhanced RTMP: el códec va como "avc1" (después del byte de multipista si lo hay).
					bool multitrack = (data[0] & 0xF) == 6;
					int fourCc = ((!multitrack) ? 1 : 2);
					if (data[fourCc] != 97 || data[fourCc + 1] != 118 || data[fourCc + 2] != 99 || data[fourCc + 3] != 49)
					{
						return 0;
					}
					record = (multitrack ? 7 : 5);
				}
				// Versión 1 y al menos un SPS; luego 2 bytes con la longitud del primer SPS.
				if (data.Length < record + 8 || data[record] != 1 || (data[record + 5] & 0x1F) == 0)
				{
					return 0;
				}
				int spsLength = (data[record + 6] << 8) | data[record + 7];
				if (data.Length < record + 8 + spsLength || spsLength < 4)
				{
					return 0;
				}
				return SpsHeight(data, record + 8, spsLength);
			}
			catch
			{
				return 0;
			}
		}

		// Lee el SPS (Sequence Parameter Set, H.264 7.3.2.1) hasta la altura y el recorte.
		private static int SpsHeight(byte[] b, int start, int len)
		{
			// Quita los bytes 03 de "escape" (00 00 03) y el byte de cabecera NAL.
			List<byte> rbsp = new List<byte>(len);
			for (int i = start + 1; i < start + len; i++)
			{
				if (i + 2 < start + len && b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 3)
				{
					rbsp.Add(0);
					rbsp.Add(0);
					i += 2;
				}
				else
				{
					rbsp.Add(b[i]);
				}
			}
			Bits bits = new Bits(rbsp.ToArray());
			int profile = bits.U(8);
			bits.U(8);
			bits.U(8);
			bits.Ue();
			int chromaFormat = 1;
			// Perfiles High y superiores: llevan formato de color, profundidad y matrices de escalado.
			if (profile == 100 || profile == 110 || profile == 122 || profile == 244 || profile == 44 || profile == 83 || profile == 86 || profile == 118 || profile == 128 || profile == 138 || profile == 139 || profile == 134 || profile == 135)
			{
				chromaFormat = bits.Ue();
				if (chromaFormat == 3)
				{
					bits.U(1);
				}
				bits.Ue();
				bits.Ue();
				bits.U(1);
				if (bits.U(1) == 1)
				{
					for (int list = 0; list < ((chromaFormat != 3) ? 8 : 12); list++)
					{
						if (bits.U(1) != 1)
						{
							continue;
						}
						// Matriz de escalado: hay que leerla para saltarla.
						int size = ((list < 6) ? 16 : 64);
						int lastScale = 8;
						int nextScale = 8;
						for (int k = 0; k < size; k++)
						{
							if (nextScale != 0)
							{
								nextScale = (lastScale + bits.Se() + 256) % 256;
							}
							lastScale = ((nextScale == 0) ? lastScale : nextScale);
						}
					}
				}
			}
			bits.Ue();
			// pic_order_cnt_type.
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
				int cycle = bits.Ue();
				for (int l = 0; l < cycle; l++)
				{
					bits.Se();
				}
				break;
			}
			}
			bits.Ue();
			bits.U(1);
			bits.Ue();
			// Altura en macrobloques (de 16 píxeles) menos 1; si no es progresivo, en pares de campos.
			int heightInMapUnits = bits.Ue();
			int frameMbsOnly = bits.U(1);
			if (frameMbsOnly == 0)
			{
				bits.U(1);
			}
			bits.U(1);
			// Recorte (por ejemplo 1088 → 1080).
			int cropTop = 0;
			int cropBottom = 0;
			if (bits.U(1) == 1)
			{
				bits.Ue();
				bits.Ue();
				cropTop = bits.Ue();
				cropBottom = bits.Ue();
			}
			int cropUnit = ((chromaFormat != 1) ? 1 : 2) * (2 - frameMbsOnly);
			if (chromaFormat == 0)
			{
				cropUnit = 2 - frameMbsOnly;
			}
			return (2 - frameMbsOnly) * (heightInMapUnits + 1) * 16 - (cropTop + cropBottom) * cropUnit;
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

		// 1080, 60 → "1080p60" (los fps solo si son 50 o más).
		public static string Label(int h, int fps)
		{
			return h + "p" + ((fps >= 50) ? fps.ToString() : "");
		}
	}
}
