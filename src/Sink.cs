using System;
using System.Collections.Generic;
using System.Threading;

namespace StreamDelay
{
	internal class Sink
	{
		private struct Item
		{
			public byte Type;

			public long Ts;

			public byte[] Data;

			public long At;
		}

		private const long MaxQueueBytes = 50331648L;

		private const long MaxLagMs = 8000L;

		public readonly Destination D;

		private readonly Engine e;

		private readonly object lk = new object();

		private readonly Queue<Item> items = new Queue<Item>();

		private long qBytes;

		private IConn conn;

		private RtmpClient pending;

		private bool connecting;

		private bool fresh;

		private long nextStartAt;

		private long startedAt;

		private int fails;

		private string outKey;

		private string lastNote;

		private bool needKey = true;

		private long tsBase = -1L;

		private long dropAudioBelow = -1L;

		private long lastRel;

		private readonly HashSet<int> started = new HashSet<int>();

		private readonly HashSet<int> tracksOut = new HashSet<int>();

		public volatile SinkState State;

		public volatile string Note = "";

		public volatile int Tracks;

		// Calidad que recibe este destino (p. ej. "1080p60"). Kick, YouTube y Otro reciben la pista de vídeo
		// de más resolución que mande OBS, sea cual sea su posición en la Transmisión mejorada.
		public volatile string Quality = "";

		private int vTrack;

		private volatile bool stopping;

		public bool AllTracks
		{
			get
			{
				if (D.IsTwitch)
				{
					return !e.UseFfmpeg;
				}
				return false;
			}
		}

		public bool Live
		{
			get
			{
				return State == SinkState.Live;
			}
		}

		public long QueueBytes
		{
			get
			{
				lock (lk)
				{
					return qBytes;
				}
			}
		}

		// Antigüedad del paquete más viejo pendiente de enviar: si crece, la subida no da para todo.
		public long QueueAgeMs
		{
			get
			{
				lock (lk)
				{
					return (items.Count == 0) ? 0L : Math.Max(0L, e.Now - items.Peek().At);
				}
			}
		}

		public Sink(Destination d, Engine e)
		{
			D = d;
			this.e = e;
		}

		private void Log(string m)
		{
			e.Log(D.Name + ": " + m);
		}

		public void Offer(FlvPacket p, long outTs, Dictionary<int, FlvPacket> seqs, long now)
		{
			bool switched = false;
			if (!AllTracks)
			{
				int m = e.MainTrack;
				if (m != vTrack)
				{
					vTrack = m;
					switched = true;
				}
				Quality = e.LabelFor(vTrack);
			}
			else
			{
				Quality = "";
			}
			if (!Wants(p))
			{
				return;
			}
			IConn conn;
			bool flag;
			lock (lk)
			{
				conn = this.conn;
				flag = fresh;
				fresh = false;
			}
			if (conn == null)
			{
				TryConnect(p.Src, now);
				return;
			}
			string text = e.KeyFor(D, p.Src);
			if (text != outKey)
			{
				Log("OBS usa otra clave: reconectando.");
				Disconnect(false);
				lock (lk)
				{
					nextStartAt = 0L;
				}
				TryConnect(p.Src, now);
				return;
			}
			if (flag)
			{
				needKey = true;
				tsBase = -1L;
				started.Clear();
				tracksOut.Clear();
				foreach (KeyValuePair<int, FlvPacket> seq in seqs)
				{
					if (seq.Value.Type == 18)
					{
						Enqueue(18, 0L, seq.Value.Data, now);
					}
				}
				foreach (KeyValuePair<int, FlvPacket> seq2 in seqs)
				{
					if (seq2.Value.Type != 18 && Wants(seq2.Value))
					{
						byte[] d = DataFor(seq2.Value);
						if (d == null)
						{
							continue;
						}
						Enqueue(seq2.Value.Type, 0L, d, now);
						if (seq2.Value.Type == 9 && tracksOut.Add(seq2.Value.Track))
						{
							Tracks = tracksOut.Count;
						}
					}
				}
			}
			else if (switched)
			{
				// Cambió la pista de más resolución (nueva sesión de OBS): enviar su cabecera y arrancar en su fotograma clave.
				Log("ahora recibe " + Quality + ".");
				foreach (KeyValuePair<int, FlvPacket> seq3 in seqs)
				{
					if (seq3.Value.Type == 9 && Wants(seq3.Value))
					{
						byte[] d2 = DataFor(seq3.Value);
						if (d2 != null)
						{
							Enqueue(9, Math.Max(0L, lastRel), d2, now);
						}
					}
				}
				started.Clear();
			}
			long num;
			if (p.Seq)
			{
				num = Math.Max(0L, lastRel);
				if (p.Type == 9 && p.SeqStart && tracksOut.Add(p.Track))
				{
					Tracks = tracksOut.Count;
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
					needKey = false;
					if (tsBase < 0)
					{
						tsBase = outTs;
					}
					dropAudioBelow = outTs;
					started.Clear();
				}
				if (p.Type == 8 && outTs < dropAudioBelow)
				{
					return;
				}
				if (p.Type == 9 && !started.Contains(p.Track))
				{
					if (!p.Key)
					{
						return;
					}
					started.Add(p.Track);
				}
				num = Math.Max(0L, outTs - tsBase);
				lastRel = Math.Max(lastRel, num);
			}
			byte[] data = DataFor(p);
			if (data == null)
			{
				return;
			}
			if (!Enqueue(p.Type, num, data, now))
			{
				Fail(conn, "la conexión con " + D.Name + " va demasiado lenta");
			}
		}

		// Twitch recibe todas las pistas; el resto, solo el vídeo de más resolución y el audio principal.
		private bool Wants(FlvPacket p)
		{
			if (AllTracks || p.Type == 18)
			{
				return true;
			}
			if (p.Type == 9)
			{
				return p.Track == vTrack;
			}
			return p.Track == 0;
		}

		// Si la mejor calidad llega en una pista extra (formato multipista), se pasa al formato normal.
		private byte[] DataFor(FlvPacket p)
		{
			if (AllTracks || p.Type != 9 || p.Track == 0)
			{
				return p.Data;
			}
			return VideoInfo.ToSingleTrack(p.Data);
		}

		private bool Enqueue(byte type, long ts, byte[] data, long now)
		{
			lock (lk)
			{
				if (conn == null)
				{
					return true;
				}
				if (qBytes > 50331648 || (items.Count > 0 && now - items.Peek().At > 8000))
				{
					return false;
				}
				items.Enqueue(new Item
				{
					Type = type,
					Ts = ts,
					Data = data,
					At = now
				});
				qBytes += data.Length;
				if (items.Count == 1)
				{
					Monitor.PulseAll(lk);
				}
				return true;
			}
		}

		public void Prepare(RtmpSession src, long now)
		{
			bool flag;
			bool flag2;
			lock (lk)
			{
				flag = conn == null && !connecting;
				flag2 = conn != null && outKey != null && e.KeyFor(D, src) != outKey;
			}
			if (flag2)
			{
				Log("OBS usa otra clave: reconectando.");
				Disconnect(true);
				flag = true;
			}
			if (flag)
			{
				TryConnect(src, now);
			}
		}

		private void TryConnect(RtmpSession src, long now)
		{
			if (!e.Running)
			{
				return;
			}
			string key = e.KeyFor(D, src);
			lock (lk)
			{
				if (conn != null || connecting || now < nextStartAt)
				{
					return;
				}
				if (D.Server.Trim().Length == 0 || key.Length == 0)
				{
					State = SinkState.NoKey;
					Note = ((D.Server.Trim().Length == 0) ? "Falta el servidor" : "Falta la clave");
					string text = ((D.Server.Trim().Length == 0) ? "falta el servidor (Ajustes → Destinos)." : (D.IsTwitch ? "falta la clave: ponla en Ajustes → Destinos o usa el servicio Twitch en OBS con tu clave." : "falta la clave: escríbela en Ajustes → Destinos."));
					if (text != lastNote)
					{
						Log(text);
					}
					lastNote = text;
					nextStartAt = now + 3000;
					return;
				}
				lastNote = null;
				connecting = true;
				State = SinkState.Connecting;
				Note = "Conectando…";
			}
			string url = D.Server.Trim().TrimEnd('/');
			Thread thread = new Thread((ThreadStart)delegate
			{
				ConnectProc(url, key);
			});
			thread.IsBackground = true;
			thread.Name = "conectar-" + D.Id;
			Thread thread2 = thread;
			thread2.Start();
		}

		private void ConnectProc(string url, string key)
		{
			IConn c = null;
			RtmpClient rc = null;
			string text = null;
			try
			{
				if (e.UseFfmpeg)
				{
					c = new FfmpegConn(e.FfmpegPath, url + "/" + key, e.Log, (string m) => Engine.Mask(m, key));
				}
				else
				{
					rc = new RtmpClient
					{
						Log = e.Log
					};
					lock (lk)
					{
						pending = rc;
					}
					rc.Connect(url, key, 10000);
					c = new RtmpConn(rc);
				}
			}
			catch (Exception ex)
			{
				text = Engine.Mask(ex.Message, key);
			}
			lock (lk)
			{
				pending = null;
			}
			if (c != null && (!e.Running || stopping))
			{
				c.Close(false);
				lock (lk)
				{
					connecting = false;
					State = SinkState.Ready;
					Note = "";
					return;
				}
			}
			if (c == null)
			{
				int num;
				lock (lk)
				{
					connecting = false;
					fails++;
					num = Math.Min(30000, 3000 << Math.Min(fails - 1, 4));
					nextStartAt = e.Now + num;
					State = SinkState.Retrying;
					Note = "Reintento en " + num / 1000 + " s";
				}
				Log("no se pudo conectar (" + text + "). Reintento en " + num / 1000 + " s.");
				if (fails == 2)
				{
					Log(D.IsTwitch ? "revisa el servidor y la clave. Con Transmisión mejorada la clave la pone OBS (servicio Twitch): vuelve a iniciar la transmisión en OBS." : "revisa el servidor y la clave (sin espacios) y que no haya otro programa emitiendo con esa clave.");
				}
				return;
			}
			lock (lk)
			{
				connecting = false;
				conn = c;
				fresh = true;
				outKey = key;
				startedAt = e.Now;
				items.Clear();
				qBytes = 0L;
				State = SinkState.Live;
				Note = "En directo";
			}
			Log(e.UseFfmpeg ? "enviando (ffmpeg, modo clásico)." : ("enviando" + (Engine.IsEnhancedKey(key) ? " con Transmisión mejorada (todas las calidades)." : (AllTracks ? "." : (" " + ((e.LabelFor(e.MainTrack).Length > 0) ? e.LabelFor(e.MainTrack) : "la calidad más alta") + " (" + D.Name + " crea el resto a partir de ella).")))));
			Thread thread = new Thread((ThreadStart)delegate
			{
				Writer(c, rc);
			});
			thread.IsBackground = true;
			thread.Name = "enviar-" + D.Id;
			Thread thread2 = thread;
			thread2.Start();
		}

		private void Writer(IConn c, RtmpClient rc)
		{
			while (true)
			{
				Item item;
				lock (lk)
				{
					while (items.Count == 0 && conn == c)
					{
						Monitor.Wait(lk, 300);
						if (rc != null && !rc.Alive && conn == c)
						{
							break;
						}
					}
					if (conn != c)
					{
						break;
					}
					if (items.Count == 0)
					{
						Monitor.Exit(lk);
						try
						{
							Fail(c, rc.Error ?? "conexión cerrada");
							break;
						}
						finally
						{
							Monitor.Enter(lk);
						}
					}
					item = items.Dequeue();
					qBytes -= item.Data.Length;
				}
				try
				{
					c.Write(item.Type, item.Ts, item.Data);
				}
				catch (Exception ex)
				{
					Fail(c, ex.Message);
					break;
				}
			}
		}

		private void Fail(IConn c, string reason)
		{
			int num;
			lock (lk)
			{
				if (conn != c)
				{
					return;
				}
				conn = null;
				items.Clear();
				qBytes = 0L;
				bool flag = e.Now - startedAt < 15000;
				fails = ((!flag) ? 1 : (fails + 1));
				num = Math.Min(30000, 3000 << Math.Min(fails - 1, 4));
				nextStartAt = e.Now + num;
				State = SinkState.Retrying;
				Note = "Reintento en " + num / 1000 + " s";
				Tracks = 0;
				Monitor.PulseAll(lk);
			}
			Log("conexión perdida (" + Engine.Mask(reason, outKey) + "). Reintento en " + num / 1000 + " s.");
			if (fails == 2)
			{
				Log("la plataforma rechaza la conexión repetidamente: revisa el servidor y la clave y que no haya otro programa emitiendo con la misma clave.");
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				c.Close(false);
			});
		}

		public void Disconnect(bool polite)
		{
			IConn conn;
			RtmpClient rtmpClient;
			lock (lk)
			{
				conn = this.conn;
				rtmpClient = pending;
				if (polite && conn != null)
				{
					long num = e.Now + 2000;
					while (items.Count > 0 && this.conn == conn && e.Now < num)
					{
						Monitor.Wait(lk, 50);
					}
				}
				this.conn = null;
				items.Clear();
				qBytes = 0L;
				fresh = false;
				Tracks = 0;
				nextStartAt = 0L;
				fails = 0;
				if (!connecting)
				{
					State = (e.Running ? SinkState.Waiting : SinkState.Ready);
					Note = (e.Running ? "Esperando" : "");
				}
				Monitor.PulseAll(lk);
			}
			needKey = true;
			tsBase = -1L;
			lastRel = 0L;
			if (rtmpClient != null)
			{
				rtmpClient.Close();
			}
			if (conn != null)
			{
				conn.Close(polite);
			}
		}

		public void Stop()
		{
			stopping = true;
			Disconnect(true);
			lock (lk)
			{
				State = SinkState.Ready;
				Note = "";
			}
		}
	}
}
