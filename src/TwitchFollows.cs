using System;

namespace StreamDelay
{
	internal class TwitchFollows : WsClient
	{
		private readonly TwitchPlatform api;

		private readonly string channel;

		private string reconnectUrl;

		private volatile bool migrating;

		protected override string Name
		{
			get
			{
				return "twitch-follows";
			}
		}

		public TwitchFollows(ChatHub h, TwitchPlatform api, string channel)
			: base(h)
		{
			this.api = api;
			this.channel = channel;
			IdleTimeoutMs = 30000;
		}

		protected override void State(string s)
		{
			hub.SetAlertState("follows", s);
		}

		protected override string Url()
		{
			string result = reconnectUrl ?? "wss://eventsub.wss.twitch.tv/ws";
			reconnectUrl = null;
			return result;
		}

		protected override void OnText(string s)
		{
			object o = Web.Parse(s);
			string text = J.S(o, "metadata", "message_type");
			switch (text)
			{
			case "session_welcome":
			{
				long num = J.N(o, 10L, "payload", "session", "keepalive_timeout_seconds");
				IdleTimeoutMs = (int)(num + 15) * 1000;
				if (migrating)
				{
					migrating = false;
					State("activo");
					return;
				}
				string sid = J.S(o, "payload", "session", "id");
				ChatHub.Background(delegate
				{
					try
					{
						if (!api.CanReadFollows())
						{
							Fatal("falta permiso: vuelve a conectar Twitch");
						}
						else
						{
							api.SubscribeFollows(sid, channel);
							State("activo");
						}
					}
					catch (ApiException ex)
					{
						if (ex.Status == 403)
						{
							Fatal("solo el dueño del canal o un moderador puede ver sus follows");
						}
						else if (ex.Status == 401)
						{
							Fatal("sesión de Twitch caducada: vuelve a conectar Twitch");
						}
						else
						{
							State("error: " + ex.Message);
						}
					}
					catch (Exception ex2)
					{
						State("error: " + ex2.Message);
					}
				});
				return;
			}
			case "session_reconnect":
				reconnectUrl = J.S(o, "payload", "session", "reconnect_url");
				migrating = true;
				try
				{
					ws.Abort();
					return;
				}
				catch
				{
					return;
				}
			case "notification":
				if (J.S(o, "metadata", "subscription_type") == "channel.follow")
				{
					string text2 = J.S(o, "payload", "event", "user_name");
					if (text2.Length == 0)
					{
						text2 = J.S(o, "payload", "event", "user_login");
					}
					hub.Emit(new ChatItem
					{
						t = "ev",
						p = "twitch",
						kind = "follow",
						user = text2,
						uid = J.S(o, "payload", "event", "user_id"),
						text = text2 + " te sigue"
					});
					return;
				}
				break;
			}
			if (text == "revocation")
			{
				Fatal("Twitch retiró el permiso de follows: vuelve a conectar Twitch");
			}
		}
	}
}
