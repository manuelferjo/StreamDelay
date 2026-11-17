// Lee valores AMF0, el formato de los comandos y metadatos RTMP: números (como double), sí/no,
// textos, objetos (como Dictionary), listas y null. Lo que no entiende lo salta.
using System;
using System.Collections.Generic;
using System.Text;

namespace StreamDelay
{
	internal class AmfReader
	{
		private readonly byte[] b;

		// Posición de lectura.
		private int p;

		public bool More
		{
			get
			{
				return p < b.Length;
			}
		}

		public AmfReader(byte[] data)
		{
			b = data;
		}

		// Lee el siguiente valor (el primer byte dice su tipo).
		public object Read()
		{
			switch (b[p++])
			{
			case 0:
			{
				// Número: double de 8 bytes, big endian.
				byte[] number = new byte[8];
				Buffer.BlockCopy(b, p, number, 0, 8);
				p += 8;
				if (BitConverter.IsLittleEndian)
				{
					Array.Reverse(number);
				}
				return BitConverter.ToDouble(number, 0);
			}
			case 1:
				// Sí/no.
				return b[p++] != 0;
			case 2:
				// Texto corto.
				return Str16();
			case 3:
				// Objeto.
				return ReadProps();
			case 5:
			case 6:
				// null / undefined.
				return null;
			case 8:
				// Array asociativo: 4 bytes de tamaño (no hace falta) y propiedades como un objeto.
				p += 4;
				return ReadProps();
			case 10:
			{
				// Array normal.
				int count = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
				p += 4;
				List<object> list = new List<object>();
				for (int i = 0; i < count; i++)
				{
					list.Add(Read());
				}
				return list;
			}
			case 11:
				// Fecha: se salta.
				p += 10;
				return null;
			case 12:
			{
				// Texto largo.
				int length = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
				p += 4;
				string result = Encoding.UTF8.GetString(b, p, length);
				p += length;
				return result;
			}
			default:
				// Tipo desconocido: no se puede seguir leyendo.
				p = b.Length;
				return null;
			}
		}

		// Texto con 2 bytes de longitud delante.
		private string Str16()
		{
			int length = (b[p] << 8) | b[p + 1];
			p += 2;
			string result = Encoding.UTF8.GetString(b, p, length);
			p += length;
			return result;
		}

		// Propiedades nombre → valor hasta el fin de objeto (00 00 09).
		private Dictionary<string, object> ReadProps()
		{
			Dictionary<string, object> props = new Dictionary<string, object>();
			while (p + 3 <= b.Length)
			{
				if (b[p] == 0 && b[p + 1] == 0 && b[p + 2] == 9)
				{
					p += 3;
					break;
				}
				string key = Str16();
				props[key] = Read();
			}
			return props;
		}
	}
}
