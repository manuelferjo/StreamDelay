using System;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	internal abstract class WsClient
	{
		protected readonly ChatHub hub;

		protected volatile bool run;

		protected ClientWebSocket ws;

		protected int IdleTimeoutMs;

		protected int PingMs;

		protected string PingText;

		private Thread th;

		private readonly object sendLock = new object();

		protected abstract string Name { get; }

		protected WsClient(ChatHub h)
		{
			hub = h;
		}

		protected abstract string Url();

		protected abstract void OnText(string s);

		protected virtual void Opened()
		{
		}

		protected abstract void State(string s);

		public void Start()
		{
			run = true;
			th = new Thread(Loop)
			{
				IsBackground = true,
				Name = "chat-" + Name
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

		protected void Fatal(string s)
		{
			State(s);
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

		private void Loop()
		{
			int num = 2000;
			while (run && hub.Alive)
			{
				try
				{
					ws = new ClientWebSocket();
					ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30.0);
					ws.ConnectAsync(new Uri(Url()), CancellationToken.None).Wait(15000);
					if (ws.State != WebSocketState.Open)
					{
						throw new Exception("no se pudo conectar");
					}
					Opened();
					if (PingMs > 0)
					{
						ClientWebSocket w = ws;
						Thread thread = new Thread((ThreadStart)delegate
						{
							while (run && w.State == WebSocketState.Open)
							{
								for (int i = 0; i < PingMs / 100; i++)
								{
									if (!run)
									{
										break;
									}
									if (w.State != WebSocketState.Open)
									{
										break;
									}
									Thread.Sleep(100);
								}
								if (PingText != null)
								{
									Send(w, PingText);
								}
							}
						});
						thread.IsBackground = true;
						thread.Start();
					}
					byte[] array = new byte[65536];
					MemoryStream memoryStream = new MemoryStream();
					while (run && ws.State == WebSocketState.Open)
					{
						WebSocketReceiveResult result;
						using (CancellationTokenSource cancellationTokenSource = ((IdleTimeoutMs > 0) ? new CancellationTokenSource(IdleTimeoutMs) : new CancellationTokenSource()))
						{
							try
							{
								result = ws.ReceiveAsync(new ArraySegment<byte>(array), cancellationTokenSource.Token).Result;
							}
							catch (AggregateException)
							{
								if (cancellationTokenSource.IsCancellationRequested)
								{
									throw new Exception("sin respuesta del servidor");
								}
								throw;
							}
						}
						if (result.MessageType == WebSocketMessageType.Close)
						{
							break;
						}
						memoryStream.Write(array, 0, result.Count);
						if (result.EndOfMessage)
						{
							string s = Encoding.UTF8.GetString(memoryStream.ToArray());
							memoryStream.SetLength(0L);
							num = 2000;
							try
							{
								OnText(s);
							}
							catch
							{
							}
						}
					}
				}
				catch (Exception ex2)
				{
					Exception ex3 = ((ex2 is AggregateException && ex2.InnerException != null) ? ex2.InnerException : ex2);
					if (run)
					{
						State("error: " + ex3.Message);
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

		protected void Send(string s)
		{
			Send(ws, s);
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

		protected static string JsonStr(string s)
		{
			return Web.ToJson(s ?? "");
		}

		protected static string Money(object amount, string currency)
		{
			string text = Convert.ToString(amount, CultureInfo.InvariantCulture);
			double result;
			if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
			{
				return (text + " " + currency).Trim();
			}
			string text2 = (currency ?? "").ToUpperInvariant();
			object obj;
			switch (text2)
			{
			default:
				obj = text2;
				break;
			case "BRL":
				obj = "R$";
				break;
			case "ARS":
				obj = "AR$";
				break;
			case "MXN":
				obj = "MX$";
				break;
			case "GBP":
				obj = "£";
				break;
			case "USD":
				obj = "$";
				break;
			case "EUR":
				obj = "€";
				break;
			}
			string text3 = (string)obj;
			return result.ToString("N2", CultureInfo.GetCultureInfo("es-ES")) + " " + text3;
		}
	}
}
