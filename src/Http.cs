using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace StreamDelay
{
	internal static class Http
	{
		private static readonly JavaScriptSerializer json;

		static Http()
		{
			json = new JavaScriptSerializer
			{
				MaxJsonLength = 8388608
			};
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			}
			catch
			{
			}
		}

		public static string Form(params string[] kv)
		{
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i + 1 < kv.Length; i += 2)
			{
				if (kv[i + 1] != null)
				{
					if (stringBuilder.Length > 0)
					{
						stringBuilder.Append('&');
					}
					stringBuilder.Append(Uri.EscapeDataString(kv[i])).Append('=').Append(Uri.EscapeDataString(kv[i + 1]));
				}
			}
			return stringBuilder.ToString();
		}

		public static string ToJson(object o)
		{
			return json.Serialize(o);
		}

		public static Dictionary<string, object> Send(string method, string url, string bearer, Dictionary<string, string> headers, string contentType, string body)
		{
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
			httpWebRequest.Method = method;
			httpWebRequest.UserAgent = "StreamDelay";
			httpWebRequest.Accept = "application/json";
			httpWebRequest.Timeout = 20000;
			httpWebRequest.ReadWriteTimeout = 20000;
			if (bearer != null)
			{
				httpWebRequest.Headers["Authorization"] = "Bearer " + bearer;
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
				httpWebRequest.ContentType = contentType;
				httpWebRequest.ContentLength = bytes.Length;
				using (Stream stream = httpWebRequest.GetRequestStream())
				{
					stream.Write(bytes, 0, bytes.Length);
				}
			}
			int statusCode;
			string text;
			try
			{
				using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
				{
					statusCode = (int)httpWebResponse.StatusCode;
					text = Read(httpWebResponse);
				}
			}
			catch (WebException ex)
			{
				HttpWebResponse httpWebResponse2 = ex.Response as HttpWebResponse;
				if (httpWebResponse2 == null)
				{
					throw new ApiException(0, "sin conexión (" + ex.Message + ")");
				}
				statusCode = (int)httpWebResponse2.StatusCode;
				text = Read(httpWebResponse2);
				httpWebResponse2.Close();
			}
			Dictionary<string, object> dictionary = null;
			if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
			{
				try
				{
					dictionary = json.DeserializeObject(text) as Dictionary<string, object>;
				}
				catch
				{
				}
			}
			if (statusCode < 200 || statusCode > 299)
			{
				throw new ApiException(statusCode, ErrorText(dictionary, text, statusCode));
			}
			return dictionary;
		}

		private static string Read(HttpWebResponse r)
		{
			using (Stream stream = r.GetResponseStream())
			{
				if (stream == null)
				{
					return "";
				}
				using (StreamReader streamReader = new StreamReader(stream, Encoding.UTF8))
				{
					return streamReader.ReadToEnd();
				}
			}
		}

		private static string ErrorText(Dictionary<string, object> d, string text, int status)
		{
			if (d != null)
			{
				if (d.ContainsKey("message") && d["message"] is string && ((string)d["message"]).Length > 0)
				{
					return (string)d["message"];
				}
				string text2 = Str(d, "error");
				if (text2.Length > 0 && !(d["error"] is Dictionary<string, object>))
				{
					string text3 = Str(d, "error_description");
					return text2 + ((text3.Length > 0) ? (": " + text3) : "");
				}
				string[] array = new string[3] { "message", "error_description", "error" };
				foreach (string key in array)
				{
					object value;
					if (d.TryGetValue(key, out value))
					{
						if (value is string && ((string)value).Length > 0)
						{
							return (string)value;
						}
						Dictionary<string, object> dictionary = value as Dictionary<string, object>;
						if (dictionary != null && dictionary.ContainsKey("message"))
						{
							return Str(dictionary, "message");
						}
					}
				}
			}
			string text4 = (text ?? "").Trim();
			return "error " + status + ((text4.Length > 0 && text4.Length < 200) ? (": " + text4) : "");
		}

		public static string Str(Dictionary<string, object> d, string k)
		{
			object value;
			if (d == null || !d.TryGetValue(k, out value) || value == null)
			{
				return "";
			}
			return Convert.ToString(value, CultureInfo.InvariantCulture);
		}

		public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
		{
			object value;
			if (d == null || !d.TryGetValue(k, out value))
			{
				return null;
			}
			return value as Dictionary<string, object>;
		}

		public static List<Dictionary<string, object>> Arr(Dictionary<string, object> d, string k)
		{
			List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
			object value;
			if (d == null || !d.TryGetValue(k, out value))
			{
				return list;
			}
			IEnumerable enumerable = value as IEnumerable;
			if (enumerable == null || value is string)
			{
				return list;
			}
			foreach (object item in enumerable)
			{
				Dictionary<string, object> dictionary = item as Dictionary<string, object>;
				if (dictionary != null)
				{
					list.Add(dictionary);
				}
			}
			return list;
		}

		public static int Int(Dictionary<string, object> d, string k, int def)
		{
			int result;
			if (!int.TryParse(Str(d, k), out result))
			{
				return def;
			}
			return result;
		}

		public static void OpenBrowser(string url)
		{
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo(url);
				processStartInfo.UseShellExecute = true;
				Process.Start(processStartInfo);
			}
			catch
			{
			}
		}
	}
}
