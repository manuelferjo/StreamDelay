using System;
using System.Collections.Generic;
using System.Threading;

namespace StreamDelay
{
	internal class ChatHub
	{
		public string TwitchChannel = "";

		public string KickChannel = "";

		public string YouTubeChannel = "";

		public TwitchPlatform TwitchApi;

		public string StreamElementsToken = "";

		public string StreamlabsToken = "";

		public volatile string FollowsState = "";

		public volatile string StreamElementsState = "";

		public volatile string StreamlabsState = "";

		private TwitchFollows follows;

		private StreamElementsAlerts se;

		private StreamlabsAlerts sl;

		public Action<string> Log = delegate
		{
		};

		public Action<ChatItem> Item = delegate
		{
		};

		public readonly Emotes Emotes = new Emotes();

		public volatile int TwitchViewers = -1;

		public volatile int KickViewers = -1;

		public volatile int YouTubeViewers = -1;

		public volatile string TwitchState = "";

		public volatile string KickState = "";

		public volatile string YouTubeState = "";

		private volatile bool running;

		private TwitchChat tw;

		private KickChat kc;

		private YouTubeChat yt;

		private Thread viewersThread;

		private readonly object stopLock = new object();

		private readonly AutoResetEvent viewersNow = new AutoResetEvent(false);

		public bool TwitchFollowsActive
		{
			get
			{
				return FollowsState == "activo";
			}
		}

		public bool Running
		{
			get
			{
				return running;
			}
		}

		internal bool Alive
		{
			get
			{
				return running;
			}
		}

		public void Start()
		{
			if (running)
			{
				return;
			}
			running = true;
			TwitchViewers = (KickViewers = (YouTubeViewers = -1));
			TwitchState = ((Clean(TwitchChannel).Length > 0) ? "conectando" : "");
			KickState = ((Clean(KickChannel).Length > 0) ? "conectando" : "");
			YouTubeState = ((YouTubeChannel.Trim().Length > 0) ? "conectando" : "");
			Background(delegate
			{
				Emotes.LoadGlobal(Log);
			});
			if (TwitchState.Length > 0)
			{
				tw = new TwitchChat(this, Clean(TwitchChannel));
				tw.Start();
			}
			if (KickState.Length > 0)
			{
				kc = new KickChat(this, Clean(KickChannel));
				kc.Start();
			}
			if (YouTubeState.Length > 0)
			{
				yt = new YouTubeChat(this, YouTubeChannel.Trim());
				yt.Start();
			}
			FollowsState = (StreamElementsState = (StreamlabsState = ""));
			if (TwitchState.Length > 0)
			{
				if (TwitchApi != null && TwitchApi.Connected)
				{
					FollowsState = "conectando";
					follows = new TwitchFollows(this, TwitchApi, Clean(TwitchChannel));
					follows.Start();
				}
				else
				{
					FollowsState = "sin conectar";
				}
			}
			if (StreamElementsToken.Trim().Length > 0)
			{
				StreamElementsState = "conectando";
				se = new StreamElementsAlerts(this, StreamElementsToken);
				se.Start();
			}
			if (StreamlabsToken.Trim().Length > 0)
			{
				StreamlabsState = "conectando";
				sl = new StreamlabsAlerts(this, StreamlabsToken);
				sl.Start();
			}
			viewersThread = new Thread(ViewersLoop)
			{
				IsBackground = true,
				Name = "chat-espectadores"
			};
			viewersThread.Start();
			Status();
		}

		public void Stop()
		{
			lock (stopLock)
			{
				running = false;
				if (tw != null)
				{
					tw.Stop();
				}
				if (kc != null)
				{
					kc.Stop();
				}
				if (yt != null)
				{
					yt.Stop();
				}
				if (follows != null)
				{
					follows.Stop();
				}
				if (se != null)
				{
					se.Stop();
				}
				if (sl != null)
				{
					sl.Stop();
				}
				tw = null;
				kc = null;
				yt = null;
				follows = null;
				se = null;
				sl = null;
				TwitchState = (KickState = (YouTubeState = ""));
				FollowsState = (StreamElementsState = (StreamlabsState = ""));
				TwitchViewers = (KickViewers = (YouTubeViewers = -1));
			}
			Status();
			Viewers();
		}

		public static string Clean(string s)
		{
			s = (s ?? "").Trim().TrimEnd('/');
			int num = s.LastIndexOf('/');
			if (num >= 0)
			{
				s = s.Substring(num + 1);
			}
			return s.TrimStart('@', '#').Trim().ToLowerInvariant();
		}

		internal void Emit(ChatItem it)
		{
			try
			{
				Item(it);
			}
			catch
			{
			}
		}

		internal void SetState(string p, string s)
		{
			string text = ((p == "twitch") ? TwitchState : ((p == "kick") ? KickState : YouTubeState));
			if (running && !(text == s))
			{
				if (p == "twitch")
				{
					TwitchState = s;
				}
				else if (p == "kick")
				{
					KickState = s;
				}
				else
				{
					YouTubeState = s;
				}
				string text2 = ((p == "twitch") ? "Twitch" : ((p == "kick") ? "Kick" : "YouTube"));
				Log("Chat de " + text2 + ": " + s + ".");
				Status();
				if (s == "conectado" || s == "sin directo")
				{
					viewersNow.Set();
				}
			}
		}

		internal void SetAlertState(string key, string s)
		{
			string text = ((key == "follows") ? FollowsState : ((key == "streamelements") ? StreamElementsState : StreamlabsState));
			if (running && !(text == s))
			{
				if (key == "follows")
				{
					FollowsState = s;
				}
				else if (key == "streamelements")
				{
					StreamElementsState = s;
				}
				else
				{
					StreamlabsState = s;
				}
				string text2 = ((key == "follows") ? "Follows de Twitch" : ((key == "streamelements") ? "StreamElements" : "Streamlabs"));
				Log(text2 + ": " + s + ".");
				Status();
			}
		}

		private void Status()
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["twitch"] = TwitchState;
			dictionary["kick"] = KickState;
			dictionary["youtube"] = YouTubeState;
			dictionary["follows"] = FollowsState;
			dictionary["streamelements"] = StreamElementsState;
			dictionary["streamlabs"] = StreamlabsState;
			Emit(new ChatItem
			{
				t = "status",
				data = dictionary
			});
		}

		private void Viewers()
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["twitch"] = TwitchViewers;
			dictionary["kick"] = KickViewers;
			dictionary["youtube"] = YouTubeViewers;
			Emit(new ChatItem
			{
				t = "viewers",
				data = dictionary
			});
		}

		private void ViewersLoop()
		{
			int num = -999;
			while (running)
			{
				TwitchChat twitchChat = tw;
				KickChat kickChat = kc;
				YouTubeChat youTubeChat = yt;
				if (twitchChat != null)
				{
					TwitchViewers = Try(twitchChat.FetchViewers);
				}
				if (kickChat != null)
				{
					KickViewers = Try(kickChat.FetchViewers);
				}
				if (youTubeChat != null)
				{
					YouTubeViewers = Try(youTubeChat.FetchViewers);
				}
				int num2 = TwitchViewers * 31 + KickViewers * 17 + YouTubeViewers;
				if (num2 != num)
				{
					num = num2;
					Viewers();
				}
				for (int i = 0; i < 30; i++)
				{
					if (!running)
					{
						break;
					}
					if (viewersNow.WaitOne(1000))
					{
						break;
					}
				}
			}
		}

		private static int Try(Func<int> f)
		{
			try
			{
				return f();
			}
			catch
			{
				return -1;
			}
		}

		internal static void Background(Action a)
		{
			Thread thread = new Thread((ThreadStart)delegate
			{
				try
				{
					a();
				}
				catch
				{
				}
			});
			thread.IsBackground = true;
			Thread thread2 = thread;
			thread2.Start();
		}
	}
}
