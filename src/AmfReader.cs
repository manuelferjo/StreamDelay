using System;
using System.Collections.Generic;
using System.Text;

namespace StreamDelay
{
	internal class AmfReader
	{
		private readonly byte[] b;

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

		public object Read()
		{
			switch (b[p++])
			{
			case 0:
			{
				byte[] array = new byte[8];
				Buffer.BlockCopy(b, p, array, 0, 8);
				p += 8;
				if (BitConverter.IsLittleEndian)
				{
					Array.Reverse(array);
				}
				return BitConverter.ToDouble(array, 0);
			}
			case 1:
				return b[p++] != 0;
			case 2:
				return Str16();
			case 3:
				return ReadProps();
			case 5:
			case 6:
				return null;
			case 8:
				p += 4;
				return ReadProps();
			case 10:
			{
				int num2 = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
				p += 4;
				List<object> list = new List<object>();
				for (int i = 0; i < num2; i++)
				{
					list.Add(Read());
				}
				return list;
			}
			case 11:
				p += 10;
				return null;
			case 12:
			{
				int num = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
				p += 4;
				string result = Encoding.UTF8.GetString(b, p, num);
				p += num;
				return result;
			}
			default:
				p = b.Length;
				return null;
			}
		}

		private string Str16()
		{
			int num = (b[p] << 8) | b[p + 1];
			p += 2;
			string result = Encoding.UTF8.GetString(b, p, num);
			p += num;
			return result;
		}

		private Dictionary<string, object> ReadProps()
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			while (p + 3 <= b.Length)
			{
				if (b[p] == 0 && b[p + 1] == 0 && b[p + 2] == 9)
				{
					p += 3;
					break;
				}
				string key = Str16();
				dictionary[key] = Read();
			}
			return dictionary;
		}
	}
}
