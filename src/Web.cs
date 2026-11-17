using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace StreamDelay
{
	internal static class Web
	{
		public const string BrowserUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

		private static readonly JavaScriptSerializer json;

		public static readonly CookieContainer YouTubeCookies;

		static Web()
		{
			json = new JavaScriptSerializer
			{
				MaxJsonLength = 16777216
			};
			YouTubeCookies = new CookieContainer();
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			}
			catch
			{
			}
			YouTubeCookies.Add(new Cookie("SOCS", "CAI", "/", ".youtube.com"));
			YouTubeCookies.Add(new Cookie("CONSENT", "YES+cb", "/", ".youtube.com"));
		}

		public static string Get(string url, CookieContainer cookies)
		{
			return Send("GET", url, null, null, cookies);
		}

		public static object GetJson(string url)
		{
			return Parse(Get(url, null));
		}

		public static object PostJson(string url, string body, Dictionary<string, string> headers, CookieContainer cookies)
		{
			return Parse(Send("POST", url, body, headers, cookies));
		}

		private static string Send(string method, string url, string body, Dictionary<string, string> headers, CookieContainer cookies)
		{
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
			httpWebRequest.Method = method;
			httpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
			httpWebRequest.Accept = "application/json, text/html;q=0.9, */*;q=0.8";
			httpWebRequest.Headers["Accept-Language"] = "es-ES,es;q=0.9,en;q=0.8";
			httpWebRequest.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
			httpWebRequest.Timeout = 15000;
			httpWebRequest.ReadWriteTimeout = 15000;
			if (cookies != null)
			{
				httpWebRequest.CookieContainer = cookies;
			}
			if (headers != null)
			{
				foreach (KeyValuePair<string, string> header in headers)
				{
					httpWebRequest.Headers[header.Key] = header.Value;
				}
			}
			if (body != null)
			{
				byte[] bytes = Encoding.UTF8.GetBytes(body);
				httpWebRequest.ContentType = "application/json";
				httpWebRequest.ContentLength = bytes.Length;
				using (Stream stream = httpWebRequest.GetRequestStream())
				{
					stream.Write(bytes, 0, bytes.Length);
				}
			}
			using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
			{
				using (StreamReader streamReader = new StreamReader(httpWebResponse.GetResponseStream(), Encoding.UTF8))
				{
					return streamReader.ReadToEnd();
				}
			}
		}

		public static object Parse(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return null;
			}
			try
			{
				return json.DeserializeObject(s);
			}
			catch
			{
				return null;
			}
		}

		public static string ToJson(object o)
		{
			return json.Serialize(o);
		}
	}
}
