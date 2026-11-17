using System;
using System.Collections.Generic;
using System.Text;

namespace StreamDelay
{
	internal class Emotes
	{
		private readonly object lk = new object();

		private Dictionary<string, string> global = new Dictionary<string, string>();

		private Dictionary<string, string> twitchCh = new Dictionary<string, string>();

		private Dictionary<string, string> kickCh = new Dictionary<string, string>();

		private Dictionary<string, string> ytCh = new Dictionary<string, string>();

		private Dictionary<string, string> mergedTw;

		private Dictionary<string, string> mergedKick;

		private Dictionary<string, string> mergedYt;

		private string twitchId = "";

		private string kickId = "";

		private string ytId = "";

		public Dictionary<string, string> For(string p)
		{
			lock (lk)
			{
				if (p == "twitch")
				{
					return mergedTw ?? (mergedTw = Merge(global, twitchCh, null));
				}
				if (p == "kick")
				{
					return mergedKick ?? (mergedKick = Merge(global, twitchCh, kickCh));
				}
				return mergedYt ?? (mergedYt = Merge(global, twitchCh, ytCh));
			}
		}

		private static Dictionary<string, string> Merge(Dictionary<string, string> a, Dictionary<string, string> b, Dictionary<string, string> c)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(a);
			foreach (KeyValuePair<string, string> item in b)
			{
				dictionary[item.Key] = item.Value;
			}
			if (c != null)
			{
				foreach (KeyValuePair<string, string> item2 in c)
				{
					dictionary[item2.Key] = item2.Value;
				}
			}
			return dictionary;
		}

		private void Set(ref Dictionary<string, string> target, Dictionary<string, string> v)
		{
			lock (lk)
			{
				target = v;
				mergedTw = (mergedKick = (mergedYt = null));
			}
		}

		public void LoadGlobal(Action<string> log)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			int num = Add7tv(dictionary, "https://7tv.io/v3/emote-sets/global", "emotes");
			int num2 = AddBttv(dictionary, "https://api.betterttv.net/3/cached/emotes/global", null);
			int num3 = AddFfz(dictionary, "https://api.betterttv.net/3/cached/frankerfacez/emotes/global");
			Set(ref global, dictionary);
			log("Emotes globales cargados: 7TV " + num + ", BTTV " + num2 + ", FFZ " + num3 + ".");
		}

		public void LoadTwitch(string id, Action<string> log)
		{
			if (id.Length != 0 && !(id == twitchId))
			{
				twitchId = id;
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				int num = Add7tv(dictionary, "https://7tv.io/v3/users/twitch/" + id, "emote_set", "emotes");
				int num2 = AddBttv(dictionary, "https://api.betterttv.net/3/cached/users/twitch/" + id, "channelEmotes") + AddBttv(dictionary, "https://api.betterttv.net/3/cached/users/twitch/" + id, "sharedEmotes");
				int num3 = AddFfz(dictionary, "https://api.betterttv.net/3/cached/frankerfacez/users/twitch/" + id);
				Set(ref twitchCh, dictionary);
				log("Emotes del canal de Twitch: 7TV " + num + ", BTTV " + num2 + ", FFZ " + num3 + ".");
			}
		}

		public void LoadKick(string userId, Action<string> log)
		{
			if (userId.Length != 0 && !(userId == kickId))
			{
				kickId = userId;
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				int num = Add7tv(dictionary, "https://7tv.io/v3/users/kick/" + userId, "emote_set", "emotes");
				Set(ref kickCh, dictionary);
				if (num > 0)
				{
					log("Emotes de 7TV del canal de Kick: " + num + ".");
				}
			}
		}

		public void LoadYouTube(string channelId, Action<string> log)
		{
			if (channelId.Length != 0 && !(channelId == ytId))
			{
				ytId = channelId;
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				int num = Add7tv(dictionary, "https://7tv.io/v3/users/youtube/" + channelId, "emote_set", "emotes");
				Set(ref ytCh, dictionary);
				if (num > 0)
				{
					log("Emotes de 7TV del canal de YouTube: " + num + ".");
				}
			}
		}

		private static int Add7tv(Dictionary<string, string> d, string url, params object[] path)
		{
			try
			{
				int num = 0;
				foreach (object item in J.L(Web.GetJson(url), path))
				{
					string text = J.S(item, "name");
					string text2 = J.S(item, "data", "host", "url");
					if (text.Length != 0 && text2.Length != 0)
					{
						d[text] = (text2.StartsWith("//") ? "https:" : "") + text2 + "/2x.webp";
						num++;
					}
				}
				return num;
			}
			catch
			{
				return 0;
			}
		}

		private static int AddBttv(Dictionary<string, string> d, string url, string key)
		{
			try
			{
				object json = Web.GetJson(url);
				int num = 0;
				foreach (object item in (key == null) ? J.L(json) : J.L(json, key))
				{
					string text = J.S(item, "code");
					string text2 = J.S(item, "id");
					if (text.Length != 0 && text2.Length != 0)
					{
						d[text] = "https://cdn.betterttv.net/emote/" + text2 + "/2x";
						num++;
					}
				}
				return num;
			}
			catch
			{
				return 0;
			}
		}

		private static int AddFfz(Dictionary<string, string> d, string url)
		{
			try
			{
				int num = 0;
				foreach (object item in J.L(Web.GetJson(url)))
				{
					string text = J.S(item, "code");
					string text2 = J.S(item, "images", "2x");
					if (text2.Length == 0)
					{
						text2 = J.S(item, "images", "1x");
					}
					if (text.Length != 0 && text2.Length != 0)
					{
						d[text] = text2;
						num++;
					}
				}
				return num;
			}
			catch
			{
				return 0;
			}
		}

		public static void AddText(List<ChatPart> parts, string text, Dictionary<string, string> map)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			StringBuilder stringBuilder = new StringBuilder();
			int num = 0;
			while (num < text.Length)
			{
				int num2 = text.IndexOf(' ', num);
				if (num2 < 0)
				{
					num2 = text.Length;
				}
				string text2 = text.Substring(num, num2 - num);
				string value;
				if (text2.Length > 0 && map != null && map.TryGetValue(text2, out value))
				{
					if (stringBuilder.Length > 0)
					{
						parts.Add(new ChatPart
						{
							x = stringBuilder.ToString()
						});
						stringBuilder.Length = 0;
					}
					parts.Add(new ChatPart
					{
						e = value,
						n = text2
					});
				}
				else
				{
					stringBuilder.Append(text2);
				}
				if (num2 < text.Length)
				{
					stringBuilder.Append(' ');
				}
				num = num2 + 1;
			}
			if (stringBuilder.Length > 0)
			{
				parts.Add(new ChatPart
				{
					x = stringBuilder.ToString()
				});
			}
		}
	}
}
