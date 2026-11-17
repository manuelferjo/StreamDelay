using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace StreamDelay
{
	internal class Engine
	{
		private const long HistoryMs = 35000L;

		public string FfmpegPath = "ffmpeg.exe";

		public int InputPort = 1935;

		public bool AllowLan;

		public List<Destination> Destinations = new List<Destination>();

		public bool UseFfmpeg;

		public Action<string> Log = delegate
		{
		};

		private List<Sink> sinks = new List<Sink>();

		private volatile int targetMs = 30000;

		private volatile bool enabled = true;

		private volatile bool running;

		private volatile bool inputActive;

		private volatile bool cutRequested;

		private volatile int currentDelayMs;

		private volatile int inputTracks;

		private long queuedBytes;

		private readonly LinkedList<FlvPacket> q = new LinkedList<FlvPacket>();

		private readonly object sync = new object();

		private readonly object sessionLock = new object();

		private readonly Stopwatch clock = Stopwatch.StartNew();

		private RtmpServer server;

		private RtmpSession current;

		private long sessionOffset;

		private Thread outThread;

		private long lastInTs = -1L;

		private readonly HashSet<int> inVideoTracks = new HashSet<int>();

		private readonly SortedDictionary<int, int> trackHeight = new SortedDictionary<int, int>();

		private int metaHeight;

		private int metaFps;

		private volatile string qualities = "";

		private volatile int topHeight;

		private string lastQualityLog;

		public volatile bool LowQualityAlert;

		// Pista de vídeo de más resolución: la que reciben Kick, YouTube y Otro.
		public volatile int MainTrack;

		private bool framesSeen;

		public volatile bool ReplayOnIncrease = true;

		public int Skips;

		private long shift;

		private long lastOut = -1L;

		private long idleSince = -1L;

		private bool needKey = true;

		private readonly Dictionary<int, FlvPacket> sentSeq = new Dictionary<int, FlvPacket>();

		private RtmpSession seqSession;

		private bool streaming;

		private readonly LinkedList<FlvPacket> history = new LinkedList<FlvPacket>();

		private volatile bool replayRequested;

		private volatile bool abortReplay;

		private List<FlvPacket> seg;

		private int segIdx;

		private long segWall;

		private long segBaseOut;

		private long segOutStart;

		private long replayL0 = -1L;

		public List<Sink> Sinks
		{
			get
			{
				return sinks;
			}
		}

		public long Now
		{
			get
			{
				return clock.ElapsedMilliseconds;
			}
		}

		public string Qualities
		{
			get
			{
				if (!inputActive)
				{
					return "";
				}
				return qualities;
			}
		}

		public int TopHeight
		{
			get
			{
				if (!inputActive)
				{
					return 0;
				}
				return topHeight;
			}
		}

		public int TargetMs
		{
			get
			{
				return targetMs;
			}
			set
			{
				int effective = Effective;
				targetMs = Math.Max(0, value);
				OnDelayChanged(effective);
			}
		}

		public bool Enabled
		{
			get
			{
				return enabled;
			}
			set
			{
				int effective = Effective;
				enabled = value;
				OnDelayChanged(effective);
			}
		}

		public bool Replaying
		{
			get
			{
				return replayL0 >= 0;
			}
		}

		private int Effective
		{
			get
			{
				if (!enabled)
				{
					return 0;
				}
				return targetMs;
			}
		}

		public bool Running
		{
			get
			{
				return running;
			}
		}

		public bool InputActive
		{
			get
			{
				return inputActive;
			}
		}

		public bool OutputActive
		{
			get
			{
				foreach (Sink sink in sinks)
				{
					if (sink.Live)
					{
						return true;
					}
				}
				return false;
			}
		}

		public int LiveCount
		{
			get
			{
				int num = 0;
				foreach (Sink sink in sinks)
				{
					if (sink.Live)
					{
						num++;
					}
				}
				return num;
			}
		}

		public long QueuedBytes
		{
			get
			{
				return Interlocked.Read(ref queuedBytes);
			}
		}

		public int CurrentDelayMs
		{
			get
			{
				if (!OutputActive)
				{
					return 0;
				}
				return currentDelayMs;
			}
		}

		public int InputTracks
		{
			get
			{
				if (!inputActive)
				{
					return 0;
				}
				return inputTracks;
			}
		}

		public int OutputTracks
		{
			get
			{
				int num = 0;
				foreach (Sink sink in sinks)
				{
					if (sink.Live)
					{
						num = Math.Max(num, sink.Tracks);
					}
				}
				return num;
			}
		}

		private void OnDelayChanged(int old)
		{
			int effective = Effective;
			if (effective > old)
			{
				replayRequested = true;
			}
			else if (effective < old)
			{
				abortReplay = true;
			}
			Wake();
		}

		public static List<string> FfmpegCandidates()
		{
			List<string> list = new List<string>();
			List<string> list2 = new List<string>();
			list2.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"));
			string[] array = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
			foreach (string text in array)
			{
				if (text.Trim().Length > 0)
				{
					list2.Add(Path.Combine(text.Trim().Trim('"'), "ffmpeg.exe"));
				}
			}
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			list2.Add(Path.Combine(folderPath, "Microsoft\\WinGet\\Links\\ffmpeg.exe"));
			list2.Add("C:\\ffmpeg\\bin\\ffmpeg.exe");
			try
			{
				string path = Path.Combine(folderPath, "Microsoft\\WinGet\\Packages");
				if (Directory.Exists(path))
				{
					string[] directories = Directory.GetDirectories(path, "Gyan.FFmpeg*");
					foreach (string path2 in directories)
					{
						list2.AddRange(Directory.GetFiles(path2, "ffmpeg.exe", SearchOption.AllDirectories));
					}
				}
			}
			catch
			{
			}
			foreach (string c in list2)
			{
				try
				{
					if (File.Exists(c) && !list.Exists((string f) => string.Equals(f, c, StringComparison.OrdinalIgnoreCase)))
					{
						list.Add(c);
					}
				}
				catch
				{
				}
			}
			return list;
		}

		public static string CheckFfmpeg(string path, bool needTls)
		{
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo(path, "-hide_banner -protocols");
				processStartInfo.UseShellExecute = false;
				processStartInfo.CreateNoWindow = true;
				processStartInfo.RedirectStandardOutput = true;
				processStartInfo.RedirectStandardError = true;
				ProcessStartInfo startInfo = processStartInfo;
				using (Process process = Process.Start(startInfo))
				{
					process.ErrorDataReceived += delegate
					{
					};
					process.BeginErrorReadLine();
					string text = process.StandardOutput.ReadToEnd();
					process.WaitForExit(5000);
					int num = text.IndexOf("Output:", StringComparison.OrdinalIgnoreCase);
					string text2 = ((num >= 0) ? text.Substring(num) : text);
					HashSet<string> hashSet = new HashSet<string>();
					string[] array = text2.Split('\n');
					foreach (string text3 in array)
					{
						hashSet.Add(text3.Trim());
					}
					if (!hashSet.Contains("rtmp"))
					{
						return "este ffmpeg no incluye RTMP";
					}
					if (needTls && !hashSet.Contains("rtmps"))
					{
						return "este ffmpeg no incluye RTMPS (necesario para este servidor)";
					}
					return null;
				}
			}
			catch (Exception ex)
			{
				return "no se pudo ejecutar (" + ex.Message + ")";
			}
		}

		public void Start()
		{
			if (running)
			{
				return;
			}
			server = new RtmpServer
			{
				Log = delegate(string m)
				{
					Log(m);
				},
				PublishStart = OnPublishStart,
				Packet = OnPacket,
				PublishEnd = OnPublishEnd
			};
			server.Start(InputPort, AllowLan);
			List<Sink> list = new List<Sink>();
			foreach (Destination destination in Destinations)
			{
				if (destination.Enabled)
				{
					list.Add(new Sink(destination, this)
					{
						State = SinkState.Waiting,
						Note = "Esperando"
					});
				}
			}
			sinks = list;
			running = true;
			ResetTimeline();
			outThread = new Thread(OutputLoop)
			{
				IsBackground = true,
				Name = "salida"
			};
			outThread.Start();
		}

		public void Stop()
		{
			running = false;
			if (server != null)
			{
				server.Stop();
			}
			Wake();
			if (outThread != null)
			{
				outThread.Join(4000);
			}
			CloseOutputs(true);
			foreach (Sink sink in sinks)
			{
				sink.Stop();
			}
			lock (sync)
			{
				q.Clear();
				queuedBytes = 0L;
			}
			inputActive = false;
			current = null;
		}

		public void CutNow()
		{
			cutRequested = true;
			Wake();
		}

		private void Wake()
		{
			lock (sync)
			{
				Monitor.PulseAll(sync);
			}
		}

		private void OnPublishStart(RtmpSession s)
		{
			RtmpSession rtmpSession;
			lock (sessionLock)
			{
				rtmpSession = current;
				current = s;
				sessionOffset = ((lastInTs >= 0) ? (lastInTs + 33) : 0);
				inVideoTracks.Clear();
				inputTracks = 0;
				lock (trackHeight)
				{
					trackHeight.Clear();
				}
				MainTrack = 0;
				framesSeen = false;
				metaHeight = (metaFps = 0);
				qualities = "";
				topHeight = 0;
				lastQualityLog = null;
			}
			if (rtmpSession != null && rtmpSession != s)
			{
				Log("Nueva conexión de OBS: sustituye a la anterior.");
				rtmpSession.Close();
			}
			inputActive = true;
			Log("OBS conectado (" + s.Remote + ")." + (IsObsKey(s.Name) ? " Usaré la clave que envía OBS." : ""));
		}

		private void OnPublishEnd(RtmpSession s)
		{
			lock (sessionLock)
			{
				if (current != s)
				{
					return;
				}
				current = null;
			}
			inputActive = false;
			Log("OBS desconectado.");
			Wake();
		}

		public static bool Classify(FlvPacket pk)
		{
			byte[] data = pk.Data;
			int num = data.Length;
			pk.SubType = -1;
			if (pk.Type == 9)
			{
				if (num < 2)
				{
					return false;
				}
				int num2 = data[0];
				int num3 = (num2 >> 4) & 7;
				if ((num2 & 0x80) != 0)
				{
					int num4 = num2 & 0xF;
					if (num4 == 6)
					{
						if (num < 7)
						{
							return false;
						}
						int num5 = data[1] & 0xF0;
						num4 = data[1] & 0xF;
						pk.Track = ((num5 == 0) ? data[6] : 0);
					}
					pk.SubType = num4;
					pk.SeqStart = num4 == 0 || num4 == 5;
					pk.Seq = pk.SeqStart || num4 == 4;
				}
				else
				{
					int num6 = num2 & 0xF;
					pk.SeqStart = (pk.Seq = (num6 == 7 || num6 == 12 || num6 == 13) && data[1] == 0);
					if (pk.Seq)
					{
						pk.SubType = 0;
					}
				}
				pk.Key = !pk.Seq && num3 == 1;
			}
			else if (pk.Type == 8)
			{
				if (num < 1)
				{
					return false;
				}
				switch (data[0] >> 4)
				{
				case 10:
					pk.SeqStart = (pk.Seq = num > 1 && data[1] == 0);
					break;
				case 9:
				{
					int num7 = data[0] & 0xF;
					if (num7 == 5)
					{
						if (num < 7)
						{
							return false;
						}
						int num8 = data[1] & 0xF0;
						num7 = data[1] & 0xF;
						pk.Track = ((num8 == 0) ? data[6] : 0);
					}
					pk.SubType = num7;
					pk.SeqStart = num7 == 0 || num7 == 4;
					pk.Seq = pk.SeqStart;
					break;
				}
				}
			}
			else
			{
				pk.SeqStart = (pk.Seq = true);
			}
			return true;
		}

		private void OnPacket(RtmpSession s, byte type, uint ts, byte[] data)
		{
			long num;
			lock (sessionLock)
			{
				if (current != s)
				{
					return;
				}
				num = sessionOffset;
			}
			FlvPacket flvPacket = new FlvPacket();
			flvPacket.Type = type;
			flvPacket.Ts = ts + num;
			flvPacket.Data = data;
			flvPacket.Arrival = clock.ElapsedMilliseconds;
			flvPacket.Src = s;
			FlvPacket flvPacket2 = flvPacket;
			if (!Classify(flvPacket2))
			{
				return;
			}
			if (type == 9 && flvPacket2.SubType == 0)
			{
				if (inVideoTracks.Add(flvPacket2.Track))
				{
					inputTracks = inVideoTracks.Count;
					if (inVideoTracks.Count == 2)
					{
						Log("Transmisión mejorada detectada: llegan varias calidades de OBS.");
					}
				}
				int num2 = VideoInfo.Height(flvPacket2);
				if (num2 > 0)
				{
					lock (trackHeight)
					{
						trackHeight[flvPacket2.Track] = num2;
					}
				}
				if (framesSeen)
				{
					UpdateQualities();
				}
			}
			else if (type == 18)
			{
				ReadMeta(data);
				if (framesSeen)
				{
					UpdateQualities();
				}
			}
			else if (type == 9 && !flvPacket2.Seq && !framesSeen)
			{
				// OBS manda todas las cabeceras antes del primer fotograma: ahora ya se conocen todas las calidades.
				framesSeen = true;
				UpdateQualities();
			}
			if (flvPacket2.Ts > lastInTs)
			{
				lastInTs = flvPacket2.Ts;
			}
			lock (sync)
			{
				bool flag = q.Count == 0;
				q.AddLast(flvPacket2);
				queuedBytes += data.Length;
				if (flag)
				{
					Monitor.PulseAll(sync);
				}
			}
		}

		private void ReadMeta(byte[] data)
		{
			try
			{
				AmfReader amfReader = new AmfReader(data);
				while (amfReader.More)
				{
					Dictionary<string, object> dictionary = amfReader.Read() as Dictionary<string, object>;
					if (dictionary != null)
					{
						object value;
						if (dictionary.TryGetValue("height", out value) && value is double)
						{
							metaHeight = (int)(double)value;
						}
						if (dictionary.TryGetValue("framerate", out value) && value is double)
						{
							metaFps = (int)Math.Round((double)value);
						}
					}
				}
			}
			catch
			{
			}
		}

		public string LabelFor(int track)
		{
			lock (trackHeight)
			{
				int h;
				if (!trackHeight.TryGetValue(track, out h))
				{
					h = ((track == 0) ? metaHeight : 0);
				}
				return (h > 0) ? VideoInfo.Label(h, (track == 0 || trackHeight.Count <= 1) ? metaFps : 0) : "";
			}
		}

		private void UpdateQualities()
		{
			List<int> list = new List<int>();
			int best = -1;
			int bestH = 0;
			lock (trackHeight)
			{
				foreach (KeyValuePair<int, int> item in trackHeight)
				{
					list.Add(item.Value);
					if (item.Value > bestH)
					{
						bestH = item.Value;
						best = item.Key;
					}
				}
				foreach (int t in inVideoTracks)
				{
					if (!trackHeight.ContainsKey(t))
					{
						Log("Aviso: no se pudo leer la resolución de la pista de vídeo " + t + " (¿códec distinto de H.264?). Kick y YouTube reciben la mejor pista H.264.");
					}
				}
			}
			MainTrack = ((best < 0) ? 0 : best);
			if (list.Count == 0 && metaHeight > 0)
			{
				list.Add(metaHeight);
			}
			if (list.Count == 0)
			{
				return;
			}
			list.Sort();
			list.Reverse();
			List<string> list2 = new List<string>();
			for (int i = 0; i < list.Count; i++)
			{
				list2.Add(VideoInfo.Label(list[i], (i == 0) ? metaFps : 0));
			}
			qualities = string.Join(" · ", list2.ToArray());
			topHeight = list[0];
			if (!(qualities == lastQualityLog))
			{
				lastQualityLog = qualities;
				Log("Calidad" + ((list.Count > 1) ? "es" : "") + " que envía OBS: " + qualities + ((list.Count > 1) ? (" (Twitch las recibe todas; Kick, YouTube y Otro, la de " + list[0] + "p).") : "."));
				if (topHeight < 1080)
				{
					LowQualityAlert = true;
					Log("ATENCIÓN: OBS envía como máximo " + topHeight + "p, así que el público no tendrá 1080p. Arréglalo en OBS → Ajustes → Vídeo → Resolución de salida (escalada): 1920x1080" + ((inVideoTracks.Count > 1) ? ", y en Emisión pon \"Ancho de banda máximo\" en Auto." : "."));
				}
			}
		}

		public static bool IsObsKey(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			string text = name.Trim();
			if (IsEnhancedKey(text))
			{
				return true;
			}
			if (text.Length >= 8)
			{
				return !string.Equals(text, "stream", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}

		public static bool IsEnhancedKey(string k)
		{
			if (!k.StartsWith("v1_", StringComparison.Ordinal))
			{
				return k.IndexOf("clientConfigId", StringComparison.OrdinalIgnoreCase) >= 0;
			}
			return true;
		}

		private static bool LooksTwitch(string k)
		{
			if (!IsEnhancedKey(k))
			{
				return k.StartsWith("live_", StringComparison.Ordinal);
			}
			return true;
		}

		public string KeyFor(Destination d, RtmpSession s)
		{
			string text = ((s != null && s.Name != null) ? s.Name.Trim() : "");
			string text2 = (d.Key ?? "").Trim();
			if (d.IsTwitch)
			{
				if (!IsObsKey(text) || (!LooksTwitch(text) && text2.Length != 0))
				{
					return text2;
				}
				return text;
			}
			if (text2.Length > 0)
			{
				return text2;
			}
			if (!IsObsKey(text) || LooksTwitch(text))
			{
				return "";
			}
			return text;
		}

		private void ResetTimeline()
		{
			needKey = true;
			lastOut = -1L;
			shift = 0L;
			history.Clear();
			EndReplay();
			streaming = false;
		}

		private void OutputLoop()
		{
			while (running)
			{
				if (abortReplay)
				{
					abortReplay = false;
					if (replayL0 >= 0)
					{
						EndReplay();
						needKey = true;
						Log("Repetición interrumpida.");
					}
				}
				if (replayRequested)
				{
					replayRequested = false;
					if (ReplayOnIncrease && OutputActive && replayL0 < 0 && StartSegment())
					{
						Log("Repitiendo los últimos " + ((double)(history.Last.Value.OutTs - segBaseOut) / 1000.0).ToString("0.0") + " s mientras sube el delay.");
					}
				}
				if (replayL0 >= 0 && StepReplay())
				{
					continue;
				}
				FlvPacket flvPacket = null;
				List<FlvPacket> list = null;
				lock (sync)
				{
					if (cutRequested)
					{
						cutRequested = false;
						q.Clear();
						queuedBytes = 0L;
						Monitor.Exit(sync);
						try
						{
							CloseOutputs(false);
							Log("Búfer vaciado y salida cortada.");
						}
						finally
						{
							Monitor.Enter(sync);
						}
						continue;
					}
					long elapsedMilliseconds = clock.ElapsedMilliseconds;
					if (q.Count == 0)
					{
						if (streaming && !inputActive)
						{
							if (idleSince < 0)
							{
								idleSince = elapsedMilliseconds;
							}
							else if (elapsedMilliseconds - idleSince > 3000)
							{
								Monitor.Exit(sync);
								try
								{
									CloseOutputs(true);
									Log("Transmisión terminada.");
								}
								finally
								{
									Monitor.Enter(sync);
								}
								idleSince = -1L;
								continue;
							}
						}
						Monitor.Wait(sync, 200);
						continue;
					}
					idleSince = -1L;
					long num = Effective;
					long num2 = q.First.Value.Arrival + num;
					if (num2 > elapsedMilliseconds)
					{
						if (num2 - elapsedMilliseconds < 4000)
						{
							foreach (Sink sink in sinks)
							{
								sink.Prepare(q.First.Value.Src, elapsedMilliseconds);
							}
						}
						Monitor.Wait(sync, (int)Math.Min(num2 - elapsedMilliseconds, 100L));
						continue;
					}
					if (elapsedMilliseconds - num2 > 1000)
					{
						LinkedListNode<FlvPacket> linkedListNode = null;
						long num3 = 0L;
						LinkedListNode<FlvPacket> linkedListNode2 = q.First;
						while (linkedListNode2 != null && linkedListNode2.Value.Arrival + num <= elapsedMilliseconds)
						{
							if (linkedListNode2.Value.Type == 9 && linkedListNode2.Value.Key && (linkedListNode == null || linkedListNode2.Value.Ts - num3 > 100))
							{
								linkedListNode = linkedListNode2;
								num3 = linkedListNode2.Value.Ts;
							}
							linkedListNode2 = linkedListNode2.Next;
						}
						if (linkedListNode != null && linkedListNode != q.First)
						{
							list = new List<FlvPacket>();
							long ts = q.First.Value.Ts;
							while (q.First != linkedListNode)
							{
								FlvPacket value = q.First.Value;
								q.RemoveFirst();
								queuedBytes -= value.Data.Length;
								if (value.Seq)
								{
									list.Add(value);
								}
							}
							needKey = true;
							Skips++;
							if (streaming)
							{
								Log("Saltados " + ((double)(linkedListNode.Value.Ts - ts) / 1000.0).ToString("0.0") + " s para volver al delay elegido.");
							}
						}
					}
					flvPacket = q.First.Value;
					q.RemoveFirst();
					queuedBytes -= flvPacket.Data.Length;
				}
				if (list != null)
				{
					foreach (FlvPacket item in list)
					{
						Send(item);
					}
				}
				Send(flvPacket);
			}
		}

		private void Send(FlvPacket p)
		{
			if (p.Src != seqSession)
			{
				seqSession = p.Src;
				sentSeq.Clear();
			}
			long num;
			if (p.Seq)
			{
				num = Math.Max(lastOut, 0L);
				if (p.SeqStart)
				{
					sentSeq[(p.Type << 16) | ((p.SubType & 0xFF) << 8) | p.Track] = p;
				}
			}
			else
			{
				if (needKey)
				{
					if (p.Type != 9 || !p.Key)
					{
						return;
					}
					shift = p.Ts - ((lastOut < 0) ? 0 : (lastOut + 33));
					needKey = false;
				}
				num = Math.Max(0L, p.Ts - shift);
			}
			Fanout(p, num);
			if (!p.Seq)
			{
				lastOut = Math.Max(lastOut, num);
				streaming = true;
				if (ReplayOnIncrease)
				{
					p.OutTs = num;
					history.AddLast(p);
					while (history.First.Value.OutTs < num - 35000)
					{
						history.RemoveFirst();
					}
				}
				else if (history.Count > 0)
				{
					history.Clear();
				}
			}
			currentDelayMs = (int)(clock.ElapsedMilliseconds - p.Arrival);
		}

		private void Fanout(FlvPacket p, long outTs)
		{
			long elapsedMilliseconds = clock.ElapsedMilliseconds;
			foreach (Sink sink in sinks)
			{
				sink.Offer(p, outTs, sentSeq, elapsedMilliseconds);
			}
		}

		private bool StartSegment()
		{
			long num;
			lock (sync)
			{
				num = ((q.Count == 0) ? Effective : (q.First.Value.Arrival + Effective - clock.ElapsedMilliseconds));
			}
			if (num < 1500 || history.Count == 0)
			{
				return false;
			}
			long outTs = history.Last.Value.OutTs;
			LinkedListNode<FlvPacket> linkedListNode = history.First;
			while (linkedListNode != null && (linkedListNode.Value.Type != 9 || !linkedListNode.Value.Key || linkedListNode.Value.OutTs < outTs - num))
			{
				linkedListNode = linkedListNode.Next;
			}
			if (linkedListNode == null || outTs - linkedListNode.Value.OutTs < 1000)
			{
				return false;
			}
			List<FlvPacket> list = new List<FlvPacket>();
			while (linkedListNode != null)
			{
				list.Add(linkedListNode.Value);
				linkedListNode = linkedListNode.Next;
			}
			if (replayL0 < 0)
			{
				replayL0 = lastOut;
			}
			seg = list;
			segIdx = 0;
			segBaseOut = list[0].OutTs;
			segOutStart = lastOut + 33;
			segWall = clock.ElapsedMilliseconds;
			return true;
		}

		private bool StepReplay()
		{
			if (!OutputActive || cutRequested)
			{
				EndReplay();
				return false;
			}
			if (segIdx >= seg.Count)
			{
				if (StartSegment())
				{
					return true;
				}
				shift -= lastOut - replayL0;
				EndReplay();
				return false;
			}
			FlvPacket flvPacket = seg[segIdx];
			long num = segWall + (flvPacket.OutTs - segBaseOut) - clock.ElapsedMilliseconds;
			if (num > 0)
			{
				lock (sync)
				{
					Monitor.Wait(sync, (int)Math.Min(num, 50L));
				}
				return true;
			}
			long num2 = segOutStart + (flvPacket.OutTs - segBaseOut);
			Fanout(flvPacket, num2);
			lastOut = Math.Max(lastOut, num2);
			segIdx++;
			return true;
		}

		private void EndReplay()
		{
			seg = null;
			replayL0 = -1L;
		}

		private void CloseOutputs(bool polite)
		{
			List<Sink> list = sinks;
			if (list.Count > 1)
			{
				List<Thread> list2 = new List<Thread>();
				foreach (Sink item in list)
				{
					Sink kk = item;
					Thread thread = new Thread((ThreadStart)delegate
					{
						kk.Disconnect(polite);
					});
					thread.IsBackground = true;
					Thread thread2 = thread;
					thread2.Start();
					list2.Add(thread2);
				}
				foreach (Thread item2 in list2)
				{
					item2.Join(5000);
				}
			}
			else
			{
				foreach (Sink item3 in list)
				{
					item3.Disconnect(polite);
				}
			}
			ResetTimeline();
		}

		public static string Mask(string m, string key)
		{
			if (string.IsNullOrEmpty(m) || string.IsNullOrEmpty(key))
			{
				return m;
			}
			string text = key;
			int num = text.IndexOf('?');
			if (num > 0)
			{
				text = text.Substring(0, num);
			}
			if (text.Length < 4)
			{
				return m;
			}
			return m.Replace(key, "(clave)").Replace(text, "(clave)");
		}
	}
}
