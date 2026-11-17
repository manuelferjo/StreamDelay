using System;
using System.Collections.Generic;

namespace StreamDelay
{
	internal class StreamlabsAlerts : WsClient
	{
		private readonly string token;

		protected override string Name
		{
			get
			{
				return "streamlabs";
			}
		}

		public StreamlabsAlerts(ChatHub h, string token)
			: base(h)
		{
			this.token = token.Trim();
			PingMs = 10000;
			PingText = "2";
		}

		protected override void State(string s)
		{
			hub.SetAlertState("streamlabs", s);
		}

		protected override string Url()
		{
			return "wss://sockets.streamlabs.com/socket.io/?token=" + Uri.EscapeDataString(token) + "&EIO=3&transport=websocket";
		}

		protected override void OnText(string s)
		{
			if (s.StartsWith("0{"))
			{
				long val = J.N(Web.Parse(s.Substring(1)), 25000L, "pingInterval");
				PingMs = (int)Math.Max(5000L, Math.Min(val, 25000L));
			}
			else if (s == "40")
			{
				State("conectado");
			}
			else if (s.StartsWith("44"))
			{
				Fatal("token incorrecto");
			}
			else
			{
				if (!s.StartsWith("42"))
				{
					return;
				}
				object o = Web.Parse(s.Substring(2));
				if (J.S(o, 0) != "event")
				{
					return;
				}
				object o2 = J.At(o, 1);
				string text = J.S(o2, "type");
				foreach (object item in J.L(o2, "message"))
				{
					if (text == "donation")
					{
						string text2 = J.S(item, "name");
						if (text2.Length == 0)
						{
							text2 = "Anónimo";
						}
						string text3 = J.S(item, "formatted_amount");
						if (text3.Length == 0)
						{
							text3 = WsClient.Money(J.At(item, "amount"), J.S(item, "currency"));
						}
						ChatItem chatItem = new ChatItem();
						chatItem.t = "ev";
						chatItem.p = "tip";
						chatItem.kind = "tip";
						chatItem.user = text2;
						chatItem.amount = text3;
						chatItem.text = text2 + " ha donado " + text3;
						ChatItem chatItem2 = chatItem;
						string text4 = J.S(item, "message");
						if (text4.Length > 0)
						{
							chatItem2.parts = new List<ChatPart>();
							Emotes.AddText(chatItem2.parts, text4, hub.Emotes.For("twitch"));
						}
						hub.Emit(chatItem2);
					}
					else if (text == "follow" && J.S(o2, "for") == "twitch_account" && !hub.TwitchFollowsActive)
					{
						string text5 = J.S(item, "name");
						if (text5.Length > 0)
						{
							hub.Emit(new ChatItem
							{
								t = "ev",
								p = "twitch",
								kind = "follow",
								user = text5,
								text = text5 + " te sigue"
							});
						}
					}
				}
			}
		}
	}
}
