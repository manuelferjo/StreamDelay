using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	internal class TwitchChat
	{
		private class Irc
		{
			public Dictionary<string, string> Tags = new Dictionary<string, string>();

			public string Prefix = "";

			public string Command = "";

			public string Channel = "";

			public string Trailing;
		}

		private readonly ChatHub hub;

		private readonly string channel;

		private volatile bool run;

		private TcpClient tcp;

		private Thread th;

		private string roomId = "";

		public TwitchChat(ChatHub h, string ch)
		{
			hub = h;
			channel = ch;
		}

		public void Start()
		{
			run = true;
			th = new Thread(Loop)
			{
				IsBackground = true,
				Name = "chat-twitch"
			};
			th.Start();
		}

		public void Stop()
		{
			run = false;
			try
			{
				if (tcp != null)
				{
					tcp.Close();
				}
			}
			catch
			{
			}
		}

		private void Loop()
		{
			int num = 2000;
			while (run && hub.Alive)
			{
				try
				{
					tcp = new TcpClient();
					tcp.Connect("irc.chat.twitch.tv", 6697);
					SslStream sslStream = new SslStream(tcp.GetStream());
					sslStream.AuthenticateAsClient("irc.chat.twitch.tv", null, SslProtocols.Tls12, false);
					StreamReader streamReader = new StreamReader(sslStream, new UTF8Encoding(false));
					StreamWriter streamWriter = new StreamWriter(sslStream, new UTF8Encoding(false));
					streamWriter.NewLine = "\r\n";
					streamWriter.AutoFlush = true;
					StreamWriter streamWriter2 = streamWriter;
					streamWriter2.WriteLine("CAP REQ :twitch.tv/tags twitch.tv/commands");
					streamWriter2.WriteLine("PASS SCHMOOPIIE");
					streamWriter2.WriteLine("NICK justinfan" + new Random().Next(10000, 99999));
					streamWriter2.WriteLine("JOIN #" + channel);
					tcp.ReceiveTimeout = 360000;
					string line;
					while (run && (line = streamReader.ReadLine()) != null)
					{
						num = 2000;
						Handle(line, streamWriter2);
					}
				}
				catch (Exception ex)
				{
					if (run)
					{
						hub.SetState("twitch", "error: " + ex.Message);
					}
				}
				try
				{
					if (tcp != null)
					{
						tcp.Close();
					}
				}
				catch
				{
				}
				if (!run)
				{
					break;
				}
				Thread.Sleep(num);
				num = Math.Min(num * 2, 30000);
			}
		}

		private static Irc Parse(string line)
		{
			Irc irc = new Irc();
			int num = 0;
			if (line.StartsWith("@"))
			{
				int num2 = line.IndexOf(' ');
				string[] array = line.Substring(1, num2 - 1).Split(';');
				foreach (string text in array)
				{
					int num3 = text.IndexOf('=');
					if (num3 < 0)
					{
						irc.Tags[text] = "";
					}
					else
					{
						irc.Tags[text.Substring(0, num3)] = Unescape(text.Substring(num3 + 1));
					}
				}
				num = num2 + 1;
			}
			if (num < line.Length && line[num] == ':')
			{
				int num4 = line.IndexOf(' ', num);
				if (num4 < 0)
				{
					return irc;
				}
				irc.Prefix = line.Substring(num + 1, num4 - num - 1);
				num = num4 + 1;
			}
			int num5 = line.IndexOf(" :", num);
			string text2 = ((num5 >= 0) ? line.Substring(num, num5 - num) : line.Substring(num));
			if (num5 >= 0)
			{
				irc.Trailing = line.Substring(num5 + 2);
			}
			string[] array2 = text2.Split(' ');
			irc.Command = array2[0];
			if (array2.Length > 1)
			{
				irc.Channel = array2[1];
			}
			return irc;
		}

		private static string Unescape(string s)
		{
			if (s.IndexOf('\\') < 0)
			{
				return s;
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < s.Length; i++)
			{
				if (s[i] != '\\' || i + 1 >= s.Length)
				{
					stringBuilder.Append(s[i]);
					continue;
				}
				char c = s[++i];
				int value;
				switch (c)
				{
				default:
					value = c;
					break;
				case 'n':
					value = 10;
					break;
				case 'r':
					value = 13;
					break;
				case ':':
					value = 59;
					break;
				case 's':
					value = 32;
					break;
				}
				stringBuilder.Append((char)value);
			}
			return stringBuilder.ToString();
		}

		private string Tag(Irc m, string k)
		{
			string value;
			if (!m.Tags.TryGetValue(k, out value))
			{
				return "";
			}
			return value;
		}

		private void Handle(string line, StreamWriter wr)
		{
			Irc irc = Parse(line);
			switch (irc.Command)
			{
			case "PING":
				wr.WriteLine("PONG :" + (irc.Trailing ?? "tmi.twitch.tv"));
				break;
			case "RECONNECT":
				throw new IOException("Twitch pidió reconectar");
			case "ROOMSTATE":
			{
				string rid = Tag(irc, "room-id");
				if (rid.Length == 0)
				{
					break;
				}
				hub.SetState("twitch", "conectado");
				if (rid != roomId)
				{
					roomId = rid;
					ChatHub.Background(delegate
					{
						hub.Emotes.LoadTwitch(rid, hub.Log);
					});
				}
				break;
			}
			case "NOTICE":
				if ((irc.Trailing ?? "").Length > 0 && Tag(irc, "msg-id") == "msg_channel_suspended")
				{
					hub.SetState("twitch", "error: canal suspendido o inexistente");
				}
				break;
			case "PRIVMSG":
				Message(irc);
				break;
			case "USERNOTICE":
				Notice(irc);
				break;
			case "CLEARMSG":
				hub.Emit(new ChatItem
				{
					t = "del",
					p = "twitch",
					id = Tag(irc, "target-msg-id")
				});
				break;
			case "CLEARCHAT":
			{
				string text = Tag(irc, "target-user-id");
				if (text.Length > 0)
				{
					hub.Emit(new ChatItem
					{
						t = "del",
						p = "twitch",
						uid = text
					});
				}
				break;
			}
			}
		}

		private string UserName(Irc m)
		{
			string text = Tag(m, "display-name");
			if (text.Length == 0)
			{
				int num = m.Prefix.IndexOf('!');
				text = ((num > 0) ? m.Prefix.Substring(0, num) : m.Prefix);
			}
			return text;
		}

		private static string[] Roles(string badges)
		{
			List<string> list = new List<string>();
			string[] array = badges.Split(',');
			foreach (string text in array)
			{
				switch (text.Split('/')[0])
				{
				case "broadcaster":
					list.Add("broadcaster");
					break;
				case "moderator":
					list.Add("mod");
					break;
				case "vip":
					list.Add("vip");
					break;
				case "subscriber":
					list.Add("sub");
					break;
				case "founder":
					list.Add("founder");
					break;
				case "partner":
				case "verified":
					list.Add("verified");
					break;
				}
			}
			return list.ToArray();
		}

		private List<ChatPart> Parts(string text, string emotesTag)
		{
			List<ChatPart> list = new List<ChatPart>();
			Dictionary<string, string> map = hub.Emotes.For("twitch");
			if (string.IsNullOrEmpty(emotesTag))
			{
				Emotes.AddText(list, text, map);
				return list;
			}
			List<int> list2 = new List<int>();
			for (int i = 0; i < text.Length; i++)
			{
				list2.Add(i);
				if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
				{
					i++;
				}
			}
			list2.Add(text.Length);
			List<KeyValuePair<int, KeyValuePair<int, string>>> list3 = new List<KeyValuePair<int, KeyValuePair<int, string>>>();
			string[] array = emotesTag.Split('/');
			foreach (string text2 in array)
			{
				int num = text2.IndexOf(':');
				if (num < 0)
				{
					continue;
				}
				string value = text2.Substring(0, num);
				string[] array2 = text2.Substring(num + 1).Split(',');
				foreach (string text3 in array2)
				{
					string[] array3 = text3.Split('-');
					int result;
					int result2;
					if (array3.Length == 2 && int.TryParse(array3[0], out result) && int.TryParse(array3[1], out result2) && result >= 0 && result <= result2 && result2 + 1 < list2.Count)
					{
						list3.Add(new KeyValuePair<int, KeyValuePair<int, string>>(list2[result], new KeyValuePair<int, string>(list2[result2 + 1], value)));
					}
				}
			}
			list3.Sort((KeyValuePair<int, KeyValuePair<int, string>> a, KeyValuePair<int, KeyValuePair<int, string>> b) => a.Key.CompareTo(b.Key));
			int num2 = 0;
			foreach (KeyValuePair<int, KeyValuePair<int, string>> item in list3)
			{
				int key = item.Key;
				int key2 = item.Value.Key;
				if (key >= num2)
				{
					Emotes.AddText(list, text.Substring(num2, key - num2), map);
					list.Add(new ChatPart
					{
						e = "https://static-cdn.jtvnw.net/emoticons/v2/" + item.Value.Value + "/default/dark/2.0",
						n = text.Substring(key, key2 - key)
					});
					num2 = key2;
				}
			}
			Emotes.AddText(list, text.Substring(num2), map);
			return list;
		}

		private ChatItem Base(Irc m, string t)
		{
			ChatItem chatItem = new ChatItem();
			chatItem.t = t;
			chatItem.p = "twitch";
			chatItem.id = Tag(m, "id");
			chatItem.uid = Tag(m, "user-id");
			chatItem.user = UserName(m);
			chatItem.color = Tag(m, "color");
			chatItem.roles = Roles(Tag(m, "badges"));
			return chatItem;
		}

		private void Message(Irc m)
		{
			string text = m.Trailing ?? "";
			if (text.StartsWith("\u0001ACTION ") && text.EndsWith("\u0001"))
			{
				text = text.Substring(8, text.Length - 9);
			}
			ChatItem chatItem = Base(m, "msg");
			chatItem.parts = Parts(text, Tag(m, "emotes"));
			string text2 = Tag(m, "bits");
			if (text2.Length > 0 && text2 != "0")
			{
				chatItem.t = "ev";
				chatItem.kind = "bits";
				chatItem.amount = text2 + " bits";
				chatItem.text = chatItem.user + " ha enviado " + text2 + " bits";
			}
			hub.Emit(chatItem);
		}

		private void Notice(Irc m)
		{
			string text = Tag(m, "msg-id");
			ChatItem chatItem = Base(m, "ev");
			string text2 = chatItem.user;
			if (Tag(m, "login") == "ananonymousgifter")
			{
				text2 = (chatItem.user = "Anónimo");
			}
			object obj;
			switch (Tag(m, "msg-param-sub-plan"))
			{
			default:
				obj = "";
				break;
			case "3000":
				obj = " de nivel 3";
				break;
			case "2000":
				obj = " de nivel 2";
				break;
			case "Prime":
				obj = " con Prime";
				break;
			}
			string text3 = (string)obj;
			switch (text)
			{
			case "sub":
				chatItem.kind = "sub";
				chatItem.text = text2 + " se ha suscrito" + text3;
				break;
			case "resub":
				chatItem.kind = "resub";
				chatItem.text = text2 + " lleva " + Tag(m, "msg-param-cumulative-months") + " meses suscrito" + text3;
				break;
			case "subgift":
				if (Tag(m, "msg-param-community-gift-id").Length > 0)
				{
					return;
				}
				chatItem.kind = "gift";
				chatItem.text = text2 + " ha regalado una suscripción a " + Tag(m, "msg-param-recipient-display-name");
				break;
			case "submysterygift":
			{
				chatItem.kind = "giftbomb";
				string text5 = (chatItem.amount = Tag(m, "msg-param-mass-gift-count"));
				chatItem.text = text2 + " ha regalado " + text5 + ((text5 == "1") ? " suscripción" : " suscripciones") + " al chat";
				break;
			}
			case "raid":
				chatItem.kind = "raid";
				chatItem.user = ((Tag(m, "msg-param-displayName").Length > 0) ? Tag(m, "msg-param-displayName") : text2);
				chatItem.amount = Tag(m, "msg-param-viewerCount");
				chatItem.text = chatItem.user + " hace raid con " + chatItem.amount + " espectadores";
				break;
			case "giftpaidupgrade":
			case "anongiftpaidupgrade":
			case "primepaidupgrade":
				chatItem.kind = "sub";
				chatItem.text = text2 + " continúa su suscripción";
				break;
			case "announcement":
				chatItem.t = "msg";
				chatItem.kind = "announce";
				break;
			default:
			{
				string text4 = Tag(m, "system-msg");
				if (text4.Length == 0)
				{
					return;
				}
				chatItem.kind = "other";
				chatItem.text = text4;
				break;
			}
			}
			if (!string.IsNullOrEmpty(m.Trailing))
			{
				chatItem.parts = Parts(m.Trailing, Tag(m, "emotes"));
			}
			hub.Emit(chatItem);
		}

		public int FetchViewers()
		{
			string body = "{\"query\":\"query{user(login:\\\"" + channel + "\\\"){id stream{viewersCount}}}\"}";
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			dictionary.Add("Client-Id", "kimne78kx3ncx6brgo4mv6wki5h1ko");
			Dictionary<string, string> headers = dictionary;
			object o = Web.PostJson("https://gql.twitch.tv/gql", body, headers, null);
			string uid = J.S(o, "data", "user", "id");
			if (uid.Length == 0)
			{
				hub.SetState("twitch", "error: el canal \"" + channel + "\" no existe");
				return -1;
			}
			if (roomId.Length == 0)
			{
				roomId = uid;
				ChatHub.Background(delegate
				{
					hub.Emotes.LoadTwitch(uid, hub.Log);
				});
			}
			if (J.At(o, "data", "user", "stream") == null)
			{
				return -2;
			}
			return (int)J.N(o, -1L, "data", "user", "stream", "viewersCount");
		}
	}
}
