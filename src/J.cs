using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StreamDelay
{
	internal static class J
	{
		public static object At(object o, params object[] path)
		{
			foreach (object obj in path)
			{
				if (o == null)
				{
					return null;
				}
				if (obj is int)
				{
					IList list = o as IList;
					int num = (int)obj;
					o = ((list != null && num >= 0 && num < list.Count) ? list[num] : null);
				}
				else
				{
					Dictionary<string, object> dictionary = o as Dictionary<string, object>;
					object value;
					o = ((dictionary != null && dictionary.TryGetValue((string)obj, out value)) ? value : null);
				}
			}
			return o;
		}

		public static string S(object o, params object[] path)
		{
			object obj = At(o, path);
			if (obj != null)
			{
				return Convert.ToString(obj, CultureInfo.InvariantCulture);
			}
			return "";
		}

		public static long N(object o, long def, params object[] path)
		{
			long result;
			if (!long.TryParse(S(o, path), out result))
			{
				return def;
			}
			return result;
		}

		public static List<object> L(object o, params object[] path)
		{
			List<object> list = new List<object>();
			IEnumerable enumerable = At(o, path) as IEnumerable;
			if (enumerable == null || enumerable is string)
			{
				return list;
			}
			foreach (object item in enumerable)
			{
				list.Add(item);
			}
			return list;
		}

		public static string Text(object o)
		{
			if (o == null)
			{
				return "";
			}
			string text = S(o, "simpleText");
			if (text.Length > 0)
			{
				return text;
			}
			StringBuilder stringBuilder = new StringBuilder();
			foreach (object item in L(o, "runs"))
			{
				stringBuilder.Append(S(item, "text"));
			}
			return stringBuilder.ToString();
		}
	}
}
