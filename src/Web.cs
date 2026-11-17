using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace StreamDelay
{
    // Peticiones web del chat (con aspecto de navegador) y JSON.
    static class Web
    {
        public const string BrowserUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
        static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        // Cookies de YouTube: evita la página de "Antes de ir a YouTube" (consentimiento) en Europa.
        public static readonly CookieContainer YouTubeCookies = new CookieContainer();

        static Web()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2
            YouTubeCookies.Add(new Cookie("SOCS", "CAI", "/", ".youtube.com"));
            YouTubeCookies.Add(new Cookie("CONSENT", "YES+cb", "/", ".youtube.com"));
        }

        public static string Get(string url, CookieContainer cookies)
        {
            return Send("GET", url, null, null, cookies);
        }

        public static object GetJson(string url) { return Parse(Get(url, null)); }

        public static object PostJson(string url, string body, Dictionary<string, string> headers, CookieContainer cookies)
        {
            return Parse(Send("POST", url, body, headers, cookies));
        }

        static string Send(string method, string url, string body, Dictionary<string, string> headers, CookieContainer cookies)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = BrowserUA;
            req.Accept = "application/json, text/html;q=0.9, */*;q=0.8";
            req.Headers["Accept-Language"] = "es-ES,es;q=0.9,en;q=0.8";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            if (cookies != null) req.CookieContainer = cookies;
            if (headers != null) foreach (var h in headers) req.Headers[h.Key] = h.Value;
            if (body != null)
            {
                byte[] b = Encoding.UTF8.GetBytes(body);
                req.ContentType = "application/json";
                req.ContentLength = b.Length;
                using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            }
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        public static object Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return json.DeserializeObject(s); } catch { return null; }
        }

        public static string ToJson(object o) { return json.Serialize(o); }
    }
}
