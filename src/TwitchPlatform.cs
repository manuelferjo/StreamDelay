using System;
using System.Collections;
using System.Collections.Generic;

namespace StreamDelay
{
	internal class TwitchPlatform : Platform
	{
		private const string Scopes = "channel:manage:broadcast moderator:read:followers user:write:chat moderator:manage:chat_messages moderator:manage:banned_users";

		public static string IdBase = "https://id.twitch.tv";

		public static string ApiBase = "https://api.twitch.tv/helix";

		private string userId = "";

		public override string Name
		{
			get
			{
				return "Twitch";
			}
		}

		public override string Key
		{
			get
			{
				return "tw";
			}
		}

		public override bool NeedsSecret
		{
			get
			{
				return false;
			}
		}

		public override string ConsoleUrl
		{
			get
			{
				return "https://dev.twitch.tv/console/apps/create";
			}
		}

		private Dictionary<string, string> H()
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			dictionary.Add("Client-Id", ClientId);
			return dictionary;
		}

		public override void Login(ILoginUi ui)
		{
			Dictionary<string, object> d = Http.Send("POST", IdBase + "/oauth2/device", null, null, "application/x-www-form-urlencoded", Http.Form("client_id", ClientId, "scopes", "channel:manage:broadcast moderator:read:followers user:write:chat moderator:manage:chat_messages moderator:manage:banned_users"));
			string url = Http.Str(d, "verification_uri");
			ui.ShowCode(Http.Str(d, "user_code"), url);
			Dictionary<string, object> tokens = PollDevice(ui, IdBase + "/oauth2/token", new string[8]
			{
				"client_id",
				ClientId,
				"scopes",
				"channel:manage:broadcast moderator:read:followers user:write:chat moderator:manage:chat_messages moderator:manage:banned_users",
				"device_code",
				Http.Str(d, "device_code"),
				"grant_type",
				"urn:ietf:params:oauth:grant-type:device_code"
			}, Http.Int(d, "interval", 5), Http.Int(d, "expires_in", 1800));
			SetTokens(tokens);
			FinishLogin();
		}

		protected override void DoRefresh()
		{
			SetTokens(Http.Send("POST", IdBase + "/oauth2/token", null, null, "application/x-www-form-urlencoded", Http.Form("client_id", ClientId, "grant_type", "refresh_token", "refresh_token", refresh)));
		}

		protected override void LoadAccount()
		{
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/users", H(), null), "data");
			if (list.Count == 0)
			{
				throw new ApiException(0, "no se encontró tu usuario de Twitch");
			}
			userId = Http.Str(list[0], "id");
			Account = Http.Str(list[0], "display_name");
			if (Account.Length == 0)
			{
				Account = Http.Str(list[0], "login");
			}
		}

		private void EnsureUser()
		{
			if (userId.Length == 0)
			{
				LoadAccount();
			}
		}

		public bool CanReadFollows()
		{
			Dictionary<string, object> dictionary = Http.Send("GET", IdBase + "/oauth2/validate", null, new Dictionary<string, string> { 
			{
				"Authorization",
				"OAuth " + Token()
			} }, null, null);
			object value;
			IEnumerable enumerable = ((dictionary != null && dictionary.TryGetValue("scopes", out value)) ? (value as IEnumerable) : null);
			if (enumerable != null)
			{
				foreach (object item in enumerable)
				{
					if ("moderator:read:followers".Equals(item))
					{
						return true;
					}
				}
			}
			return false;
		}

		private string BroadcasterId(string channelLogin)
		{
			EnsureUser();
			if (string.IsNullOrEmpty(channelLogin) || channelLogin.Equals(Account, StringComparison.OrdinalIgnoreCase))
			{
				return userId;
			}
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/users?login=" + Uri.EscapeDataString(channelLogin), H(), null), "data");
			if (list.Count == 0)
			{
				throw new ApiException(404, "el canal \"" + channelLogin + "\" no existe");
			}
			return Http.Str(list[0], "id");
		}

		public void SendChat(string channelLogin, string text)
		{
			string value = BroadcasterId(channelLogin);
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary.Add("broadcaster_id", value);
			dictionary.Add("sender_id", userId);
			dictionary.Add("message", text);
			Dictionary<string, object> body = dictionary;
			Dictionary<string, object> d;
			try
			{
				d = Api("POST", ApiBase + "/chat/messages", H(), body);
			}
			catch (ApiException ex)
			{
				if (ex.Status == 401 && ex.Message.ToLowerInvariant().Contains("scope"))
				{
					throw new ApiException(401, "falta permiso: vuelve a conectar Twitch");
				}
				throw;
			}
			List<Dictionary<string, object>> list = Http.Arr(d, "data");
			if (list.Count > 0 && Http.Str(list[0], "is_sent").ToLowerInvariant() == "false")
			{
				string text2 = Http.Str(Http.Obj(list[0], "drop_reason"), "message");
				throw new ApiException(400, (text2.Length > 0) ? text2 : "Twitch no envió el mensaje");
			}
		}

		// ---------- Moderación (borrar mensajes, expulsar temporalmente o banear) ----------

		private static ApiException ModError(ApiException ex)
		{
			if (ex.Status == 401 || ex.Status == 403)
			{
				return new ApiException(ex.Status, "falta permiso de moderación: vuelve a conectar Twitch en la pestaña Título (o no eres moderador de ese canal)");
			}
			return ex;
		}

		public void DeleteMessage(string channelLogin, string messageId)
		{
			string b = BroadcasterId(channelLogin);
			try
			{
				Api("DELETE", ApiBase + "/moderation/chat?broadcaster_id=" + b + "&moderator_id=" + userId + "&message_id=" + Uri.EscapeDataString(messageId), H(), null);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// seconds = 0: ban permanente.
		public void Ban(string channelLogin, string targetUserId, int seconds, string reason)
		{
			string b = BroadcasterId(channelLogin);
			Dictionary<string, object> data = new Dictionary<string, object>();
			data.Add("user_id", targetUserId);
			if (seconds > 0)
			{
				data.Add("duration", Math.Max(1, Math.Min(1209600, seconds)));
			}
			if (!string.IsNullOrEmpty(reason))
			{
				data.Add("reason", reason);
			}
			Dictionary<string, object> body = new Dictionary<string, object>();
			body.Add("data", data);
			try
			{
				Api("POST", ApiBase + "/moderation/bans?broadcaster_id=" + b + "&moderator_id=" + userId, H(), body);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		public void SubscribeFollows(string sessionId, string channelLogin)
		{
			string value = BroadcasterId(channelLogin);
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary.Add("type", "channel.follow");
			dictionary.Add("version", "2");
			dictionary.Add("condition", new Dictionary<string, object>
			{
				{ "broadcaster_user_id", value },
				{ "moderator_user_id", userId }
			});
			dictionary.Add("transport", new Dictionary<string, object>
			{
				{ "method", "websocket" },
				{ "session_id", sessionId }
			});
			Dictionary<string, object> body = dictionary;
			Api("POST", ApiBase + "/eventsub/subscriptions", H(), body);
		}

		public override List<Category> Search(string q)
		{
			List<Category> list = new List<Category>();
			foreach (Dictionary<string, object> item in Http.Arr(Api("GET", ApiBase + "/search/categories?first=12&query=" + Uri.EscapeDataString(q), H(), null), "data"))
			{
				list.Add(new Category(Http.Str(item, "id"), Http.Str(item, "name")));
			}
			return list;
		}

		public override void ReadCurrent(out string title, out Category cat)
		{
			EnsureUser();
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/channels?broadcaster_id=" + userId, H(), null), "data");
			title = ((list.Count > 0) ? Http.Str(list[0], "title") : "");
			cat = ((list.Count > 0 && Http.Str(list[0], "game_id").Length > 0) ? new Category(Http.Str(list[0], "game_id"), Http.Str(list[0], "game_name")) : null);
		}

		public override void Update(string title, Category cat)
		{
			EnsureUser();
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			if (title != null)
			{
				dictionary["title"] = title;
			}
			if (cat != null)
			{
				dictionary["game_id"] = cat.Id;
			}
			Api("PATCH", ApiBase + "/channels?broadcaster_id=" + userId, H(), dictionary);
		}
	}
}
