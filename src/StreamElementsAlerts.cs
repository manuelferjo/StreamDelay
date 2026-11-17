using System;
using System.Collections.Generic;

namespace StreamDelay
{
	internal class StreamElementsAlerts : WsClient
	{
		private readonly string token;

		protected override string Name
		{
			get
			{
				return "streamelements";
			}
		}

		public StreamElementsAlerts(ChatHub h, string token)
			: base(h)
		{
			this.token = token.Trim();
		}

		protected override void State(string s)
		{
			hub.SetAlertState("streamelements", s);
		}

		protected override string Url()
		{
			return "wss://astro.streamelements.com";
		}

		protected override void OnText(string s)
		{
			object o = Web.Parse(s);
			switch (J.S(o, "type"))
			{
			case "welcome":
				Send("{\"type\":\"subscribe\",\"nonce\":\"" + Guid.NewGuid().ToString("N") + "\",\"data\":{\"topic\":\"channel.activities\",\"token\":" + WsClient.JsonStr(token) + ",\"token_type\":\"jwt\"}}");
				break;
			case "response":
			{
				string text = J.S(o, "error");
				if (text == "err_unauthorized")
				{
					Fatal("token incorrecto o caducado");
				}
				else if (text.Length > 0)
				{
					State("error: " + text);
				}
				else
				{
					State("conectado");
				}
				break;
			}
			case "message":
				if (J.S(o, "topic") == "channel.activities")
				{
					Activity(J.At(o, "data"));
				}
				break;
			}
		}

		private void Activity(object a)
		{
			string text = J.S(a, "type");
			object o = J.At(a, "data");
			string text2 = J.S(o, "displayName");
			if (text2.Length == 0)
			{
				text2 = J.S(o, "username");
			}
			if (text == "tip")
			{
				if (text2.Length == 0)
				{
					text2 = "Anónimo";
				}
				string text3 = WsClient.Money(J.At(o, "amount"), J.S(o, "currency"));
				ChatItem chatItem = new ChatItem();
				chatItem.t = "ev";
				chatItem.p = "tip";
				chatItem.kind = "tip";
				chatItem.user = text2;
				chatItem.amount = text3;
				chatItem.text = text2 + " ha donado " + text3;
				ChatItem chatItem2 = chatItem;
				string text4 = J.S(o, "message");
				if (text4.Length > 0)
				{
					chatItem2.parts = new List<ChatPart>();
					Emotes.AddText(chatItem2.parts, text4, hub.Emotes.For("twitch"));
				}
				hub.Emit(chatItem2);
			}
			else if (text == "follower" && J.S(a, "provider") == "twitch" && !hub.TwitchFollowsActive)
			{
				hub.Emit(new ChatItem
				{
					t = "ev",
					p = "twitch",
					kind = "follow",
					user = text2,
					text = text2 + " te sigue"
				});
			}
		}
	}
}
