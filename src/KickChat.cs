using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace StreamDelay
{
	internal class KickChat
	{
		private const string FallbackKey = "34bf7a0ff419a2a775b9";

		private const string FallbackCluster = "us2";

		private readonly ChatHub hub;

		private readonly string slug;

		private volatile bool run;

		private ClientWebSocket ws;

		private Thread th;

		private readonly object sendLock = new object();

		private string chatroomId = "";

		private string channelId = "";

		private string userId = "";

		private static readonly Regex EmoteRx = new Regex("\\[emote:(\\d+):([^\\]]*)\\]");

		public KickChat(ChatHub h, string s)
		{
			hub = h;
			slug = s;
		}

		public void Start()
		{
			run = true;
			th = new Thread(Loop)
			{
				IsBackground = true,
				Name = "chat-kick"
			};
			th.Start();
		}

		public void Stop()
		{
			run = false;
			try
			{
				if (ws != null)
				{
					ws.Abort();
				}
			}
			catch
			{
			}
		}

		private object Channel()
		{
			return Web.GetJson("https://kick.com/api/v2/channels/" + Uri.EscapeDataString(slug));
		}

		private void Loop()
		{
			int num = 2000;
			while (run && hub.Alive)
			{
				try
				{
					if (chatroomId.Length == 0)
					{
						object o;
						try
						{
							o = Channel();
						}
						catch (WebException ex)
						{
							HttpWebResponse httpWebResponse = ex.Response as HttpWebResponse;
							if (httpWebResponse != null && httpWebResponse.StatusCode == HttpStatusCode.NotFound)
							{
								throw new Exception("el canal \"" + slug + "\" no existe");
							}
							if (httpWebResponse != null && (httpWebResponse.StatusCode == HttpStatusCode.Forbidden || (int)httpWebResponse.StatusCode == 429))
							{
								throw new Exception("Kick ha bloqueado la consulta del canal (protección anti-bots); se reintenta solo");
							}
							throw;
						}
						chatroomId = J.S(o, "chatroom", "id");
						channelId = J.S(o, "id");
						userId = J.S(o, "user_id");
						if (chatroomId.Length == 0)
						{
							throw new Exception("Kick no devolvió el chat del canal");
						}
						string uid = userId;
						ChatHub.Background(delegate
						{
							hub.Emotes.LoadKick(uid, hub.Log);
						});
					}
					ws = new ClientWebSocket();
					ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30.0);
					ws.ConnectAsync(new Uri(PusherUrl()), CancellationToken.None).Wait(15000);
					if (ws.State != WebSocketState.Open)
					{
						throw new Exception("no se pudo conectar con el chat de Kick");
					}
					Thread thread = new Thread((ThreadStart)delegate
					{
						ClientWebSocket clientWebSocket = ws;
						while (run && clientWebSocket.State == WebSocketState.Open)
						{
							for (int i = 0; i < 600; i++)
							{
								if (!run)
								{
									break;
								}
								if (clientWebSocket.State != WebSocketState.Open)
								{
									break;
								}
								Thread.Sleep(100);
							}
							Send(clientWebSocket, "{\"event\":\"pusher:ping\",\"data\":{}}");
						}
					});
					thread.IsBackground = true;
					Thread thread2 = thread;
					thread2.Start();
					byte[] array = new byte[65536];
					MemoryStream memoryStream = new MemoryStream();
					while (run && ws.State == WebSocketState.Open)
					{
						WebSocketReceiveResult result = ws.ReceiveAsync(new ArraySegment<byte>(array), CancellationToken.None).Result;
						if (result.MessageType != WebSocketMessageType.Close)
						{
							memoryStream.Write(array, 0, result.Count);
							if (result.EndOfMessage)
							{
								string text = Encoding.UTF8.GetString(memoryStream.ToArray());
								memoryStream.SetLength(0L);
								num = 2000;
								Handle(text);
							}
							continue;
						}
						break;
					}
				}
				catch (Exception ex2)
				{
					Exception ex3 = ((ex2 is AggregateException && ex2.InnerException != null) ? ex2.InnerException : ex2);
					if (run)
					{
						hub.SetState("kick", "error: " + ex3.Message);
					}
				}
				try
				{
					if (ws != null)
					{
						ws.Abort();
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

		private string PusherUrl()
		{
			string text = "34bf7a0ff419a2a775b9";
			string text2 = "us2";
			try
			{
				string body = string.Concat("{\"client\":{\"id\":\"", Guid.NewGuid(), "\",\"type\":\"web\"},\"capabilities\":{\"accepted_providers\":[{\"provider\":\"pusher\"}]}}");
				object o = Web.PostJson("https://web.kick.com/api/v1/realtime/channels/" + channelId + "/chat/connection", body, null, null);
				foreach (object item in J.L(o, "data", "connections"))
				{
					if (!(J.S(item, "provider") != "pusher"))
					{
						string text3 = J.S(item, "credentials", "app_key");
						string text4 = J.S(item, "credentials", "cluster");
						if (text3.Length > 0 && text4.Length > 0)
						{
							text = text3;
							text2 = text4;
							break;
						}
					}
				}
			}
			catch
			{
			}
			return "wss://ws-" + text2 + ".pusher.com/app/" + text + "?protocol=7&client=js&version=8.4.0&flash=false";
		}

		private void Send(ClientWebSocket w, string s)
		{
			try
			{
				byte[] bytes = Encoding.UTF8.GetBytes(s);
				lock (sendLock)
				{
					w.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(10000);
				}
			}
			catch
			{
			}
		}

		private void Subscribe(string channel)
		{
			Send(ws, "{\"event\":\"pusher:subscribe\",\"data\":{\"auth\":\"\",\"channel\":\"" + channel + "\"}}");
		}

		private void Handle(string text)
		{
			object o = Web.Parse(text);
			string text2 = J.S(o, "event");
			object obj = J.At(o, "data");
			if (obj is string)
			{
				obj = Web.Parse((string)obj);
			}
			switch (text2)
			{
			case "pusher:connection_established":
				Subscribe("chatrooms." + chatroomId + ".v2");
				Subscribe("chatrooms." + chatroomId);
				if (channelId.Length > 0)
				{
					Subscribe("channel." + channelId);
				}
				return;
			case "pusher_internal:subscription_succeeded":
				hub.SetState("kick", "conectado");
				return;
			case "pusher:ping":
				Send(ws, "{\"event\":\"pusher:pong\",\"data\":{}}");
				return;
			case "pusher:error":
				hub.SetState("kick", "error: " + J.S(obj, "message"));
				return;
			case "App\\Events\\ChatMessageEvent":
				Message(obj);
				return;
			case "App\\Events\\MessageDeletedEvent":
				hub.Emit(new ChatItem
				{
					t = "del",
					p = "kick",
					id = J.S(obj, "message", "id")
				});
				return;
			case "App\\Events\\UserBannedEvent":
				hub.Emit(new ChatItem
				{
					t = "del",
					p = "kick",
					uid = J.S(obj, "user", "id")
				});
				return;
			case "App\\Events\\SubscriptionEvent":
			{
				string text7 = J.S(obj, "username");
				long num = J.N(obj, 1L, "months");
				Event("sub", text7, (num > 1) ? (text7 + " lleva " + num + " meses suscrito") : (text7 + " se ha suscrito"), null);
				return;
			}
			case "App\\Events\\GiftedSubscriptionsEvent":
			{
				string text6 = J.S(obj, "gifter_username");
				if (text6.Length == 0)
				{
					text6 = "Anónimo";
				}
				int count = J.L(obj, "gifted_usernames").Count;
				Event((count > 1) ? "giftbomb" : "gift", text6, text6 + " ha regalado " + count + ((count == 1) ? " suscripción" : " suscripciones"), count.ToString());
				return;
			}
			case "App\\Events\\StreamHostEvent":
			{
				string text4 = J.S(obj, "host_username");
				string text5 = J.S(obj, "number_viewers");
				Event("raid", text4, text4 + " hace host con " + text5 + " espectadores", text5);
				return;
			}
			case "App\\Events\\FollowersUpdated":
			{
				string text3 = J.S(obj, "username");
				if (text3.Length > 0 && J.S(obj, "followed").ToLowerInvariant() == "true")
				{
					Event("follow", text3, text3 + " te sigue", null);
				}
				return;
			}
			}
			if (text2.IndexOf("Kicks", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("Gift", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				string text8 = J.S(obj, "sender", "username");
				string text9 = J.S(obj, "gift", "amount");
				if (text8.Length > 0 && text9.Length > 0)
				{
					Event("kicks", text8, text8 + " ha enviado " + text9 + " Kicks", text9 + " Kicks");
				}
			}
		}

		private void Event(string kind, string user, string text, string amount)
		{
			hub.Emit(new ChatItem
			{
				t = "ev",
				p = "kick",
				kind = kind,
				user = user,
				text = text,
				amount = amount
			});
		}

		private void Message(object d)
		{
			string text = J.S(d, "content");
			List<ChatPart> list = new List<ChatPart>();
			Dictionary<string, string> map = hub.Emotes.For("kick");
			int num = 0;
			foreach (Match item in EmoteRx.Matches(text))
			{
				Emotes.AddText(list, text.Substring(num, item.Index - num), map);
				list.Add(new ChatPart
				{
					e = "https://files.kick.com/emotes/" + item.Groups[1].Value + "/fullsize",
					n = item.Groups[2].Value
				});
				num = item.Index + item.Length;
			}
			Emotes.AddText(list, text.Substring(num), map);
			List<string> list2 = new List<string>();
			foreach (object item2 in J.L(d, "sender", "identity", "badges"))
			{
				switch (J.S(item2, "type"))
				{
				case "broadcaster":
					list2.Add("broadcaster");
					break;
				case "moderator":
					list2.Add("mod");
					break;
				case "vip":
					list2.Add("vip");
					break;
				case "subscriber":
					list2.Add("sub");
					break;
				case "founder":
					list2.Add("founder");
					break;
				case "og":
					list2.Add("og");
					break;
				case "verified":
					list2.Add("verified");
					break;
				}
			}
			hub.Emit(new ChatItem
			{
				t = "msg",
				p = "kick",
				id = J.S(d, "id"),
				uid = J.S(d, "sender", "id"),
				user = J.S(d, "sender", "username"),
				color = J.S(d, "sender", "identity", "color"),
				roles = list2.ToArray(),
				parts = list
			});
		}

		public int FetchViewers()
		{
			object o = Channel();
			object obj = J.At(o, "livestream");
			if (obj == null)
			{
				return -2;
			}
			return (int)J.N(obj, -1L, "viewer_count");
		}
	}
}
