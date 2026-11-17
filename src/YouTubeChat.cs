using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace StreamDelay
{
	internal class YouTubeChat
	{
		private readonly ChatHub hub;

		private readonly string input;

		private volatile bool run;

		private Thread th;

		private volatile string videoId = "";

		private volatile string clientVersion = "2.20250101.00.00";

		private string channelId = "";

		public YouTubeChat(ChatHub h, string s)
		{
			hub = h;
			input = s;
		}

		public void Start()
		{
			run = true;
			th = new Thread(Loop)
			{
				IsBackground = true,
				Name = "chat-youtube"
			};
			th.Start();
		}

		public void Stop()
		{
			run = false;
		}

		private string PageUrl()
		{
			string text = input.Trim();
			Match match = Regex.Match(text, "(?:v=|youtu\\.be/|/live/|/shorts/)([\\w-]{11})");
			if (match.Success)
			{
				return "https://www.youtube.com/watch?v=" + match.Groups[1].Value;
			}
			if (Regex.IsMatch(text, "^[\\w-]{11}$") && !text.StartsWith("@"))
			{
				return "https://www.youtube.com/watch?v=" + text;
			}
			match = Regex.Match(text, "(UC[\\w-]{22})");
			if (match.Success)
			{
				return "https://www.youtube.com/channel/" + match.Groups[1].Value + "/live";
			}
			match = Regex.Match(text, "@([\\w.\\-]+)");
			if (match.Success)
			{
				return "https://www.youtube.com/@" + match.Groups[1].Value + "/live";
			}
			match = Regex.Match(text, "youtube\\.com/(c/|user/)?([\\w.\\-]+)");
			if (match.Success)
			{
				return "https://www.youtube.com/" + match.Groups[1].Value + match.Groups[2].Value + "/live";
			}
			return "https://www.youtube.com/@" + text.TrimStart('@') + "/live";
		}

		private void Loop()
		{
			while (run && hub.Alive)
			{
				string text = null;
				try
				{
					string text2 = Web.Get(PageUrl(), Web.YouTubeCookies);
					Match match = Regex.Match(text2, "<link rel=\"canonical\" href=\"https://www\\.youtube\\.com/watch\\?v=([\\w-]{11})\"");
					Match match2 = Regex.Match(text2, "\"INNERTUBE_CLIENT_VERSION\":\"([^\"]+)\"");
					Match match3 = Regex.Match(text2, "\"reloadContinuationData\":\\{\"continuation\":\"([^\"]+)\"");
					Match match4 = Regex.Match(text2, "\"channelId\":\"(UC[\\w-]{22})\"");
					if (match2.Success)
					{
						clientVersion = match2.Groups[1].Value;
					}
					if (match4.Success && match4.Groups[1].Value != channelId)
					{
						channelId = match4.Groups[1].Value;
						string cid = channelId;
						ChatHub.Background(delegate
						{
							hub.Emotes.LoadYouTube(cid, hub.Log);
						});
					}
					if (text2.Contains("consent.youtube.com/") && !match2.Success)
					{
						throw new Exception("YouTube pide aceptar cookies; vuelve a intentarlo");
					}
					if (!match.Success || !match3.Success || !text2.Contains("\"isLiveNow\":true"))
					{
						videoId = "";
						hub.SetState("youtube", "sin directo");
					}
					else
					{
						videoId = match.Groups[1].Value;
						text = LiveChatContinuation(videoId) ?? match3.Groups[1].Value;
					}
				}
				catch (Exception ex)
				{
					hub.SetState("youtube", "error: " + ex.Message);
				}
				if (text != null)
				{
					hub.SetState("youtube", "conectado");
					Poll(text);
				}
				for (int num = 0; num < 300; num++)
				{
					if (!run)
					{
						break;
					}
					Thread.Sleep(100);
				}
			}
		}

		private string LiveChatContinuation(string v)
		{
			try
			{
				string text = Web.Get("https://www.youtube.com/live_chat?is_popout=1&v=" + v, Web.YouTubeCookies);
				MatchCollection matchCollection = Regex.Matches(text, "\"selected\":(?:true|false),\"continuation\":\\{\"reloadContinuationData\":\\{\"continuation\":\"([^\"]+)\"");
				if (matchCollection.Count >= 2)
				{
					return matchCollection[1].Groups[1].Value;
				}
				if (matchCollection.Count == 1)
				{
					return matchCollection[0].Groups[1].Value;
				}
			}
			catch
			{
			}
			return null;
		}

		private string Ctx()
		{
			return "{\"client\":{\"clientName\":\"WEB\",\"clientVersion\":\"" + clientVersion + "\",\"hl\":\"es\",\"gl\":\"ES\"}}";
		}

		private void Poll(string cont)
		{
			bool flag = true;
			int num = 0;
			while (run && hub.Alive && cont != null)
			{
				int val = 3000;
				try
				{
					object o = Web.PostJson("https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?prettyPrint=false", "{\"context\":" + Ctx() + ",\"continuation\":\"" + cont + "\"}", null, Web.YouTubeCookies);
					object obj = J.At(o, "continuationContents", "liveChatContinuation");
					if (obj == null)
					{
						hub.SetState("youtube", "sin directo");
						return;
					}
					string text = null;
					foreach (object item in J.L(obj, "continuations"))
					{
						object obj2 = J.At(item, "invalidationContinuationData") ?? J.At(item, "timedContinuationData") ?? J.At(item, "reloadContinuationData");
						if (obj2 != null)
						{
							text = J.S(obj2, "continuation");
							val = (int)J.N(obj2, 3000L, "timeoutMs");
						}
					}
					if (!flag)
					{
						foreach (object item2 in J.L(obj, "actions"))
						{
							Action(item2);
						}
					}
					flag = false;
					cont = text;
					num = 0;
				}
				catch (Exception ex)
				{
					if (++num >= 5)
					{
						hub.SetState("youtube", "error: " + ex.Message);
						return;
					}
				}
				val = Math.Max(1000, Math.Min(val, 8000));
				for (int i = 0; i < val / 100; i++)
				{
					if (!run)
					{
						break;
					}
					Thread.Sleep(100);
				}
			}
			hub.SetState("youtube", "sin directo");
		}

		private void Action(object a)
		{
			object obj = J.At(a, "addChatItemAction", "item");
			if (obj != null)
			{
				object r;
				if ((r = J.At(obj, "liveChatTextMessageRenderer")) != null)
				{
					hub.Emit(Base(r, "msg", null));
				}
				else if ((r = J.At(obj, "liveChatPaidMessageRenderer")) != null)
				{
					ChatItem chatItem = Base(r, "ev", "superchat");
					chatItem.amount = J.Text(J.At(r, "purchaseAmountText"));
					chatItem.text = chatItem.user + " ha enviado un Super Chat de " + chatItem.amount;
					chatItem.color = Argb(J.N(r, 0L, "bodyBackgroundColor"));
					hub.Emit(chatItem);
				}
				else if ((r = J.At(obj, "liveChatPaidStickerRenderer")) != null)
				{
					ChatItem chatItem2 = Base(r, "ev", "sticker");
					chatItem2.amount = J.Text(J.At(r, "purchaseAmountText"));
					chatItem2.text = chatItem2.user + " ha enviado un Super Sticker de " + chatItem2.amount;
					chatItem2.parts = null;
					hub.Emit(chatItem2);
				}
				else if ((r = J.At(obj, "liveChatMembershipItemRenderer")) != null)
				{
					ChatItem chatItem3 = Base(r, "ev", "member");
					string text = J.Text(J.At(r, "headerPrimaryText"));
					string text2 = J.Text(J.At(r, "headerSubtext"));
					chatItem3.text = chatItem3.user + ": " + ((text.Length > 0) ? text : ((text2.Length > 0) ? text2 : "nuevo miembro"));
					hub.Emit(chatItem3);
				}
				else if ((r = J.At(obj, "liveChatSponsorshipsGiftPurchaseAnnouncementRenderer")) != null)
				{
					object o = J.At(r, "header", "liveChatSponsorshipsHeaderRenderer");
					ChatItem chatItem4 = new ChatItem();
					chatItem4.t = "ev";
					chatItem4.p = "youtube";
					chatItem4.kind = "giftmember";
					chatItem4.id = J.S(r, "id");
					chatItem4.user = J.Text(J.At(o, "authorName"));
					ChatItem chatItem5 = chatItem4;
					chatItem5.text = chatItem5.user + " " + J.Text(J.At(o, "primaryText"));
					hub.Emit(chatItem5);
				}
				return;
			}
			string text3 = J.S(a, "markChatItemAsDeletedAction", "targetItemId");
			if (text3.Length > 0)
			{
				hub.Emit(new ChatItem
				{
					t = "del",
					p = "youtube",
					id = text3
				});
				return;
			}
			string text4 = J.S(a, "markChatItemsByAuthorAsDeletedAction", "externalChannelId");
			if (text4.Length > 0)
			{
				hub.Emit(new ChatItem
				{
					t = "del",
					p = "youtube",
					uid = text4
				});
			}
		}

		private ChatItem Base(object r, string t, string kind)
		{
			ChatItem chatItem = new ChatItem();
			chatItem.t = t;
			chatItem.p = "youtube";
			chatItem.kind = kind;
			chatItem.id = J.S(r, "id");
			chatItem.uid = J.S(r, "authorExternalChannelId");
			chatItem.user = J.Text(J.At(r, "authorName")).TrimStart('@');
			ChatItem chatItem2 = chatItem;
			List<string> list = new List<string>();
			foreach (object item in J.L(r, "authorBadges"))
			{
				object o = J.At(item, "liveChatAuthorBadgeRenderer");
				switch (J.S(o, "icon", "iconType"))
				{
				case "OWNER":
					list.Add("broadcaster");
					continue;
				case "MODERATOR":
					list.Add("mod");
					continue;
				case "VERIFIED":
					list.Add("verified");
					continue;
				}
				if (J.At(o, "customThumbnail") != null)
				{
					list.Add("member");
				}
			}
			chatItem2.roles = list.ToArray();
			List<ChatPart> list2 = new List<ChatPart>();
			Dictionary<string, string> map = hub.Emotes.For("youtube");
			foreach (object item2 in J.L(r, "message", "runs"))
			{
				object obj = J.At(item2, "emoji");
				if (obj == null)
				{
					Emotes.AddText(list2, J.S(item2, "text"), map);
					continue;
				}
				bool flag = J.S(obj, "isCustomEmoji").ToLowerInvariant() == "true";
				List<object> list3 = J.L(obj, "image", "thumbnails");
				string n = J.S(obj, "shortcuts", 0);
				if (flag && list3.Count > 0)
				{
					list2.Add(new ChatPart
					{
						e = J.S(list3[list3.Count - 1], "url"),
						n = n
					});
				}
				else
				{
					list2.Add(new ChatPart
					{
						x = J.S(obj, "emojiId")
					});
				}
			}
			if (list2.Count > 0)
			{
				chatItem2.parts = list2;
			}
			return chatItem2;
		}

		private static string Argb(long v)
		{
			if (v == 0)
			{
				return null;
			}
			return "#" + ((v >> 16) & 0xFF).ToString("X2") + ((v >> 8) & 0xFF).ToString("X2") + (v & 0xFF).ToString("X2");
		}

		public int FetchViewers()
		{
			string text = videoId;
			if (text.Length == 0)
			{
				return -2;
			}
			object o = Web.PostJson("https://www.youtube.com/youtubei/v1/updated_metadata?prettyPrint=false", "{\"context\":" + Ctx() + ",\"videoId\":\"" + text + "\"}", null, Web.YouTubeCookies);
			foreach (object item in J.L(o, "actions"))
			{
				string s = J.S(item, "updateViewershipAction", "viewCount", "videoViewCountRenderer", "originalViewCount");
				long result;
				if (long.TryParse(s, out result))
				{
					return (int)result;
				}
			}
			return -1;
		}
	}
}
