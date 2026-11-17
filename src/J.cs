using System;
using System.Collections.Generic;
using System.Text;

namespace StreamDelay
{
    // Acceso cómodo a JSON deserializado: J.At(o, "a", "b", 0, "c").
    static class J
    {
        public static object At(object o, params object[] path)
        {
            foreach (var k in path)
            {
                if (o == null) return null;
                if (k is int)
                {
                    var l = o as System.Collections.IList;
                    int i = (int)k;
                    o = l != null && i >= 0 && i < l.Count ? l[i] : null;
                }
                else
                {
                    var d = o as Dictionary<string, object>;
                    object v;
                    o = d != null && d.TryGetValue((string)k, out v) ? v : null;
                }
            }
            return o;
        }

        public static string S(object o, params object[] path)
        {
            var v = At(o, path);
            return v == null ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static long N(object o, long def, params object[] path)
        {
            long r;
            return long.TryParse(S(o, path), out r) ? r : def;
        }

        public static List<object> L(object o, params object[] path)
        {
            var res = new List<object>();
            var l = At(o, path) as System.Collections.IEnumerable;
            if (l == null || l is string) return res;
            foreach (var x in l) res.Add(x);
            return res;
        }

        // Textos de YouTube: {"simpleText": "..."} o {"runs": [{"text": ...}]}.
        public static string Text(object o)
        {
            if (o == null) return "";
            string s = S(o, "simpleText");
            if (s.Length > 0) return s;
            var sb = new StringBuilder();
            foreach (var r in L(o, "runs")) sb.Append(S(r, "text"));
            return sb.ToString();
        }
    }
}
