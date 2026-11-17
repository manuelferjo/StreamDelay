using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	internal class KickPlatform : Platform
	{
		public const int CallbackPort = 17563;

		private const string Scopes = "user:read channel:read channel:write chat:write moderation:ban moderation:chat_message:manage";

		public static string IdBase = "https://id.kick.com";

		public static string ApiBase = "https://api.kick.com/public";

		public static string RedirectUri
		{
			get
			{
				return "http://localhost:" + 17563 + "/callback";
			}
		}

		public override string Name
		{
			get
			{
				return "Kick";
			}
		}

		public override string Key
		{
			get
			{
				return "kick";
			}
		}

		public override bool NeedsSecret
		{
			get
			{
				return true;
			}
		}

		public override string ConsoleUrl
		{
			get
			{
				return "https://kick.com/settings/developer";
			}
		}

		private static string B64Url(byte[] b)
		{
			return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-')
				.Replace('/', '_');
		}

		public override void Login(ILoginUi ui)
		{
			byte[] array = new byte[32];
			using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
			{
				randomNumberGenerator.GetBytes(array);
			}
			string text = B64Url(array);
			string text2;
			using (SHA256 sHA = SHA256.Create())
			{
				text2 = B64Url(sHA.ComputeHash(Encoding.ASCII.GetBytes(text)));
			}
			byte[] array2 = new byte[16];
			using (RandomNumberGenerator randomNumberGenerator2 = RandomNumberGenerator.Create())
			{
				randomNumberGenerator2.GetBytes(array2);
			}
			string text3 = B64Url(array2);
			string value = null;
			string text4 = null;
			List<TcpListener> list = new List<TcpListener>();
			IPAddress[] array3 = new IPAddress[2]
			{
				IPAddress.Loopback,
				IPAddress.IPv6Loopback
			};
			foreach (IPAddress localaddr in array3)
			{
				try
				{
					TcpListener tcpListener = new TcpListener(localaddr, 17563);
					tcpListener.Start();
					list.Add(tcpListener);
				}
				catch
				{
				}
			}
			if (list.Count == 0)
			{
				throw new ApiException(0, "el puerto " + 17563 + " está ocupado; cierra el programa que lo use y vuelve a intentarlo");
			}
			try
			{
				string url = IdBase + "/oauth/authorize?" + Http.Form("response_type", "code", "client_id", ClientId, "redirect_uri", RedirectUri, "scope", "user:read channel:read channel:write chat:write moderation:ban moderation:chat_message:manage", "code_challenge", text2, "code_challenge_method", "S256", "state", text3);
				ui.ShowBrowserWait(url);
				Http.OpenBrowser(url);
				DateTime dateTime = DateTime.UtcNow.AddMinutes(5.0);
				while (value == null && text4 == null)
				{
					if (ui.Cancelled)
					{
						throw new OperationCanceledException();
					}
					if (DateTime.UtcNow > dateTime)
					{
						throw new ApiException(0, "tiempo agotado esperando la autorización");
					}
					TcpListener tcpListener2 = null;
					foreach (TcpListener item in list)
					{
						if (item.Pending())
						{
							tcpListener2 = item;
							break;
						}
					}
					if (tcpListener2 == null)
					{
						Thread.Sleep(100);
						continue;
					}
					using (TcpClient tcpClient = tcpListener2.AcceptTcpClient())
					{
						tcpClient.ReceiveTimeout = 5000;
						NetworkStream stream = tcpClient.GetStream();
						string text5 = ReadLine(stream);
						string q = "";
						string[] array4 = text5.Split(' ');
						if (array4.Length >= 2 && array4[1].StartsWith("/callback"))
						{
							int num = array4[1].IndexOf('?');
							if (num >= 0)
							{
								q = array4[1].Substring(num + 1);
							}
							Dictionary<string, string> dictionary = ParseQuery(q);
							string value2;
							dictionary.TryGetValue("state", out value2);
							if (dictionary.ContainsKey("error"))
							{
								text4 = dictionary["error"];
								goto IL_0366;
							}
							if (value2 != text3)
							{
								Reply(stream, 400, "Respuesta no válida. Vuelve a intentarlo desde StreamDelay.");
								continue;
							}
							if (dictionary.TryGetValue("code", out value))
							{
								goto IL_0366;
							}
							Reply(stream, 400, "Falta el código.");
						}
						else
						{
							Reply(stream, 404, "No encontrado");
						}
						goto end_IL_0261;
						IL_0366:
						Reply(stream, 200, (text4 == null) ? "¡Listo! StreamDelay ya está conectado a Kick. Puedes cerrar esta pestaña." : ("Kick no ha dado permiso (" + text4 + "). Puedes cerrar esta pestaña."));
						end_IL_0261:;
					}
				}
			}
			finally
			{
				foreach (TcpListener item2 in list)
				{
					try
					{
						item2.Stop();
					}
					catch
					{
					}
				}
			}
			if (text4 != null)
			{
				throw new ApiException(0, (text4 == "access_denied") ? "has rechazado el acceso" : ("Kick devolvió: " + text4));
			}
			SetTokens(Http.Send("POST", IdBase + "/oauth/token", null, null, "application/x-www-form-urlencoded", Http.Form("grant_type", "authorization_code", "code", value, "client_id", ClientId, "client_secret", ClientSecret, "redirect_uri", RedirectUri, "code_verifier", text)));
			FinishLogin();
		}

		private static string ReadLine(Stream s)
		{
			StringBuilder stringBuilder = new StringBuilder();
			int num;
			while ((num = s.ReadByte()) >= 0 && num != 10 && stringBuilder.Length < 8192)
			{
				if (num != 13)
				{
					stringBuilder.Append((char)num);
				}
			}
			return stringBuilder.ToString();
		}

		private static Dictionary<string, string> ParseQuery(string q)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			string[] array = q.Split('&');
			foreach (string text in array)
			{
				int num = text.IndexOf('=');
				if (num > 0)
				{
					dictionary[Uri.UnescapeDataString(text.Substring(0, num).Replace('+', ' '))] = Uri.UnescapeDataString(text.Substring(num + 1).Replace('+', ' '));
				}
			}
			return dictionary;
		}

		private static void Reply(Stream s, int code, string msg)
		{
			string s2 = "<!doctype html><meta charset=utf-8><title>StreamDelay</title><body style=\"background:#09090C;color:#F4F4F5;font:18px 'Segoe UI',sans-serif;display:grid;place-items:center;height:90vh\"><div style=\"max-width:520px;text-align:center\"><div style=\"font-size:28px;font-weight:700;color:#C09AFD;margin-bottom:12px\">Stream Delay</div>" + WebUtility.HtmlEncode(msg) + "</div>";
			byte[] bytes = Encoding.UTF8.GetBytes(s2);
			string s3 = "HTTP/1.1 " + code + " OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n";
			byte[] bytes2 = Encoding.ASCII.GetBytes(s3);
			try
			{
				s.Write(bytes2, 0, bytes2.Length);
				s.Write(bytes, 0, bytes.Length);
				s.Flush();
			}
			catch
			{
			}
		}

		protected override void DoRefresh()
		{
			SetTokens(Http.Send("POST", IdBase + "/oauth/token", null, null, "application/x-www-form-urlencoded", Http.Form("grant_type", "refresh_token", "refresh_token", refresh, "client_id", ClientId, "client_secret", ClientSecret)));
		}

		protected override void LoadAccount()
		{
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/v1/channels", null, null), "data");
			if (list.Count > 0)
			{
				Account = Http.Str(list[0], "slug");
				if (Account.Length > 0)
				{
					return;
				}
			}
			List<Dictionary<string, object>> list2 = Http.Arr(Api("GET", ApiBase + "/v1/users", null, null), "data");
			Account = ((list2.Count > 0) ? Http.Str(list2[0], "name") : "");
		}

		public override List<Category> Search(string q)
		{
			List<Category> list = new List<Category>();
			foreach (Dictionary<string, object> item in Http.Arr(Api("GET", ApiBase + "/v2/categories?limit=12&name=" + Uri.EscapeDataString(q), null, null), "data"))
			{
				list.Add(new Category(Http.Str(item, "id"), Http.Str(item, "name")));
			}
			return list;
		}

		public override void ReadCurrent(out string title, out Category cat)
		{
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/v1/channels", null, null), "data");
			title = ((list.Count > 0) ? Http.Str(list[0], "stream_title") : "");
			Dictionary<string, object> dictionary = ((list.Count > 0) ? Http.Obj(list[0], "category") : null);
			cat = ((dictionary != null && Http.Str(dictionary, "id").Length > 0 && Http.Str(dictionary, "id") != "0") ? new Category(Http.Str(dictionary, "id"), Http.Str(dictionary, "name")) : null);
		}

		public override void Update(string title, Category cat)
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			if (title != null)
			{
				dictionary["stream_title"] = title;
			}
			long result;
			if (cat != null && long.TryParse(cat.Id, out result))
			{
				dictionary["category_id"] = result;
			}
			Api("PATCH", ApiBase + "/v1/channels", null, dictionary);
		}

		public void SendChat(string slug, string text)
		{
			string url = ApiBase + "/v1/channels" + ((string.IsNullOrEmpty(slug) || slug.Equals(Account, StringComparison.OrdinalIgnoreCase)) ? "" : ("?slug=" + Uri.EscapeDataString(slug)));
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", url, null, null), "data");
			if (list.Count == 0)
			{
				throw new ApiException(404, "el canal \"" + slug + "\" no existe");
			}
			long result;
			long.TryParse(Http.Str(list[0], "broadcaster_user_id"), out result);
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary.Add("type", "user");
			dictionary.Add("content", text);
			dictionary.Add("broadcaster_user_id", result);
			Dictionary<string, object> body = dictionary;
			Dictionary<string, object> d;
			try
			{
				d = Api("POST", ApiBase + "/v1/chat", null, body);
			}
			catch (ApiException ex)
			{
				if (ex.Status == 401 || ex.Status == 403)
				{
					throw new ApiException(ex.Status, "falta permiso de chat: marca chat:write en tu app de Kick y vuelve a conectar Kick");
				}
				throw;
			}
			Dictionary<string, object> dictionary2 = Http.Obj(d, "data");
			if (dictionary2 != null && Http.Str(dictionary2, "is_sent").ToLowerInvariant() == "false")
			{
				throw new ApiException(400, "Kick no envió el mensaje");
			}
		}
			// ---------- Moderación ----------

		private static ApiException ModError(ApiException ex)
		{
			if (ex.Status == 401 || ex.Status == 403)
			{
				return new ApiException(ex.Status, "falta permiso de moderación: marca moderation:ban y moderation:chat_message:manage en tu app de Kick y vuelve a conectar Kick");
			}
			return ex;
		}

		private long ChannelUserId(string slug)
		{
			string url = ApiBase + "/v1/channels" + ((string.IsNullOrEmpty(slug) || slug.Equals(Account, StringComparison.OrdinalIgnoreCase)) ? "" : ("?slug=" + Uri.EscapeDataString(slug)));
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", url, null, null), "data");
			if (list.Count == 0)
			{
				throw new ApiException(404, "el canal \"" + slug + "\" no existe");
			}
			long result;
			long.TryParse(Http.Str(list[0], "broadcaster_user_id"), out result);
			return result;
		}

		public void DeleteMessage(string messageId)
		{
			try
			{
				Api("DELETE", ApiBase + "/v1/chat/" + Uri.EscapeDataString(messageId), null, null);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// seconds = 0: ban permanente. Kick cuenta las expulsiones en minutos (de 1 a 10080).
		public void Ban(string slug, string targetUserId, int seconds, string reason)
		{
			long b = ChannelUserId(slug);
			long uid;
			long.TryParse(targetUserId, out uid);
			Dictionary<string, object> body = new Dictionary<string, object>();
			body.Add("broadcaster_user_id", b);
			body.Add("user_id", uid);
			if (seconds > 0)
			{
				body.Add("duration", Math.Max(1, Math.Min(10080, (seconds + 59) / 60)));
			}
			if (!string.IsNullOrEmpty(reason))
			{
				body.Add("reason", reason.Length > 100 ? reason.Substring(0, 100) : reason);
			}
			try
			{
				Api("POST", ApiBase + "/v1/moderation/bans", null, body);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}
	}
}
