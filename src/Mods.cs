// Enlaces para mods: cada mod tiene su enlace (con su nombre y una clave que se puede quitar) a un
// chat conjunto solo para moderar, desde cualquier sitio (por internet, con el túnel de ngrok).
// - Moderan con TUS cuentas (las de la pestaña Título); en el Registro queda qué mod hizo cada cosa.
// - Escriben con SUS cuentas: cada mod las conecta desde su enlace (Twitch y YouTube con un código,
//   Kick pegando la dirección a la que le lleva Kick). Los tokens se guardan cifrados en tu PC.
// Solo funciona mientras StreamDelay está abierto.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class ModUser
	{
		public string Name = "";

		public string Key = "";

		// Sus cuentas (para escribir). Se crean con la app de cada plataforma de la pestaña Título.
		public TwitchPlatform Tw;

		public KickPlatform Kc;

		public YouTubePlatform Yt;

		// Inicio de sesión a medias (código de Twitch o YouTube).
		public string TwDevice = "";

		public string YtDevice = "";
	}

	internal partial class MainForm
	{
		// Permisos que se piden a las cuentas de los mods: solo escribir en el chat.
		private const string ModTwitchScopes = "user:write:chat";

		private const string ModKickScopes = "user:read channel:read chat:write";

		private readonly List<ModUser> mods = new List<ModUser>();

		private readonly NgrokTunnel tunnel = new NgrokTunnel();

		private string ngrokToken = "";

		private bool ngrokOn;

		private static string NewModKey()
		{
			const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
			byte[] b = new byte[20];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(b);
			}
			StringBuilder sb = new StringBuilder();
			foreach (byte x in b)
			{
				sb.Append(chars[x % chars.Length]);
			}
			return sb.ToString();
		}

		private ModUser ModByKey(string key)
		{
			lock (mods)
			{
				foreach (ModUser m in mods)
				{
					if (m.Key == key)
					{
						return m;
					}
				}
			}
			return null;
		}

		private string ModLink(ModUser m)
		{
			string url = tunnel.Url;
			return (url.Length > 0) ? (url + "/mod?k=" + m.Key) : "";
		}

		// Arranca o para el túnel según los ajustes (al abrir StreamDelay, al cambiar algo o el puerto).
		private void UpdateTunnel()
		{
			bool any;
			lock (mods)
			{
				any = mods.Count > 0;
			}
			if (ngrokOn && ngrokToken.Length > 0 && any && chatServer != null && chatServer.Running)
			{
				tunnel.Start(chatServer.Port, ngrokToken);
			}
			else if (tunnel.Running)
			{
				tunnel.Stop();
			}
		}

		// ---------- Lo que piden las páginas de los mods ----------

		private static readonly string[] ModModeration = new string[12] { "del", "t60", "t600", "t3600", "t86400", "ban", "unban", "hide", "ignore", "modes", "setmode", "clear" };

		private Dictionary<string, object> ModApi(string key, string name, Dictionary<string, object> d)
		{
			Dictionary<string, object> res = new Dictionary<string, object>();
			ModUser m = ModByKey(key);
			if (m == null)
			{
				res["error"] = "enlace no válido";
				return res;
			}
			string a = J.S(d, "a");
			string p = J.S(d, "p");
			if (Array.IndexOf(ModModeration, a) >= 0)
			{
				// Moderar: con tus cuentas, apuntando qué mod lo ha hecho.
				d["by"] = m.Name;
				return ModAction(d);
			}
			try
			{
				switch (a)
				{
				case "info":
					return ModInfo(m);
				case "login":
					return ModLogin(m, p);
				case "loginpoll":
					return ModLoginPoll(m, p);
				case "loginfinish":
				{
					ModKick(m).FinishPasteLogin(J.S(d, "url"));
					SaveConfigLater();
					LogLater("Mod " + m.Name + ": ha conectado su Kick (" + m.Kc.Account + ").");
					return ModInfo(m);
				}
				case "logout":
				{
					Platform pl = (p == "twitch") ? (Platform)m.Tw : ((p == "kick") ? (Platform)m.Kc : m.Yt);
					if (pl != null)
					{
						pl.Logout();
					}
					SaveConfigLater();
					return ModInfo(m);
				}
				case "send":
					return ModSend(m, J.S(d, "text"), J.L(d, "to"));
				default:
					res["error"] = "acción desconocida";
					return res;
				}
			}
			catch (Exception ex)
			{
				res["error"] = ex.Message;
				return res;
			}
		}

		private void SaveConfigLater()
		{
			try
			{
				BeginInvoke((Action)delegate
				{
					SaveConfig();
				});
			}
			catch
			{
			}
		}

		private TwitchPlatform ModTwitch(ModUser m)
		{
			if (m.Tw == null)
			{
				m.Tw = new TwitchPlatform();
			}
			m.Tw.ClientId = twitch.ClientId;
			return m.Tw;
		}

		private KickPlatform ModKick(ModUser m)
		{
			if (m.Kc == null)
			{
				m.Kc = new KickPlatform();
			}
			m.Kc.ClientId = kick.ClientId;
			m.Kc.ClientSecret = kick.ClientSecret;
			return m.Kc;
		}

		private YouTubePlatform ModYouTube(ModUser m)
		{
			if (m.Yt == null)
			{
				m.Yt = new YouTubePlatform();
			}
			m.Yt.ClientId = youtube.ClientId;
			m.Yt.ClientSecret = youtube.ClientSecret;
			return m.Yt;
		}

		// Qué cuentas tiene conectadas el mod y a qué chats puede escribir.
		private Dictionary<string, object> ModInfo(ModUser m)
		{
			string twCh = "";
			string kcCh = "";
			try
			{
				Invoke((Action)delegate
				{
					twCh = ChatHub.Clean(fldChatTw.Box.Text);
					kcCh = ChatHub.Clean(fldChatKick.Box.Text);
				});
			}
			catch
			{
			}
			Dictionary<string, object> acc = new Dictionary<string, object>();
			Dictionary<string, object> send = new Dictionary<string, object>();
			Dictionary<string, object> can = new Dictionary<string, object>();
			acc["twitch"] = (m.Tw != null && m.Tw.Connected) ? m.Tw.Account : "";
			acc["kick"] = (m.Kc != null && m.Kc.Connected) ? m.Kc.Account : "";
			acc["youtube"] = (m.Yt != null && m.Yt.Connected) ? m.Yt.Account : "";
			// Se puede conectar si tú tienes la app de esa plataforma puesta en la pestaña Título.
			can["twitch"] = twitch.Configured;
			can["kick"] = kick.Configured;
			can["youtube"] = youtube.Configured;
			send["twitch"] = ((string)acc["twitch"]).Length > 0 && twCh.Length > 0;
			send["kick"] = ((string)acc["kick"]).Length > 0 && kcCh.Length > 0;
			send["youtube"] = ((string)acc["youtube"]).Length > 0 && youtube.Connected;
			Dictionary<string, object> r = new Dictionary<string, object>();
			r["ok"] = true;
			r["name"] = m.Name;
			r["accounts"] = acc;
			r["can"] = can;
			r["send"] = send;
			return r;
		}

		private Dictionary<string, object> ModLogin(ModUser m, string p)
		{
			Dictionary<string, object> r = new Dictionary<string, object>();
			if (p == "twitch")
			{
				if (!twitch.Configured)
				{
					throw new ApiException(0, "el streamer no tiene Twitch configurado en StreamDelay");
				}
				Dictionary<string, object> s = ModTwitch(m).BeginDevice(ModTwitchScopes);
				m.TwDevice = (string)s["device"];
				r["code"] = s["code"];
				r["url"] = s["url"];
			}
			else if (p == "youtube")
			{
				if (!youtube.Configured)
				{
					throw new ApiException(0, "el streamer no tiene YouTube configurado en StreamDelay");
				}
				Dictionary<string, object> s = ModYouTube(m).BeginDevice();
				m.YtDevice = (string)s["device"];
				r["code"] = s["code"];
				r["url"] = s["url"];
			}
			else if (p == "kick")
			{
				if (!kick.Configured)
				{
					throw new ApiException(0, "el streamer no tiene Kick configurado en StreamDelay");
				}
				r["url"] = ModKick(m).BeginPasteLogin(ModKickScopes);
				r["paste"] = true;
			}
			else
			{
				throw new ApiException(0, "plataforma desconocida");
			}
			r["ok"] = true;
			return r;
		}

		private Dictionary<string, object> ModLoginPoll(ModUser m, string p)
		{
			bool done;
			if (p == "twitch")
			{
				done = m.TwDevice.Length > 0 && ModTwitch(m).PollDeviceOnce(m.TwDevice, ModTwitchScopes);
				if (done)
				{
					m.TwDevice = "";
				}
			}
			else if (p == "youtube")
			{
				done = m.YtDevice.Length > 0 && ModYouTube(m).PollDeviceOnce(m.YtDevice);
				if (done)
				{
					m.YtDevice = "";
				}
			}
			else
			{
				throw new ApiException(0, "plataforma desconocida");
			}
			if (!done)
			{
				Dictionary<string, object> r = new Dictionary<string, object>();
				r["ok"] = true;
				r["pending"] = true;
				return r;
			}
			SaveConfigLater();
			Platform pl = (p == "twitch") ? (Platform)m.Tw : m.Yt;
			LogLater("Mod " + m.Name + ": ha conectado su " + pl.Name + " (" + pl.Account + ").");
			return ModInfo(m);
		}

		private Dictionary<string, object> ModSend(ModUser m, string text, List<object> to)
		{
			Dictionary<string, object> res = new Dictionary<string, object>();
			text = (text ?? "").Trim();
			if (text.Length == 0)
			{
				res["error"] = "mensaje vacío";
				return res;
			}
			string twCh = "";
			string kcCh = "";
			try
			{
				Invoke((Action)delegate
				{
					twCh = ChatHub.Clean(fldChatTw.Box.Text);
					kcCh = ChatHub.Clean(fldChatKick.Box.Text);
				});
			}
			catch
			{
			}
			List<string> done = new List<string>();
			foreach (object o in to)
			{
				string p = Convert.ToString(o);
				try
				{
					if (p == "twitch" && m.Tw != null && m.Tw.Connected)
					{
						m.Tw.SendChat(twCh, text);
					}
					else if (p == "kick" && m.Kc != null && m.Kc.Connected)
					{
						m.Kc.SendChat(kcCh, text);
					}
					else if (p == "youtube" && m.Yt != null && m.Yt.Connected)
					{
						// Al chat de TU directo (lo busca tu cuenta), con la cuenta del mod.
						m.Yt.SendChatTo(youtube.CurrentLiveChatId(), text);
					}
					else
					{
						res[p] = "conecta tu cuenta de esa plataforma en «Mis cuentas»";
						continue;
					}
					res[p] = "ok";
					done.Add(p);
				}
				catch (Exception ex)
				{
					res[p] = ex.Message;
				}
			}
			if (done.Count > 0)
			{
				LogLater("Mod " + m.Name + " ha escrito en el chat (" + string.Join(", ", done.ToArray()) + ").");
			}
			return res;
		}

		// ---------- Ventana "Enlaces para mods" ----------

		private void ModsDialog()
		{
			Form f = DarkForm("Enlaces para mods", 460, 640);
			Card c1 = new Card
			{
				Title = "Conexión por internet",
				Subtitle = "Con ngrok (gratis). El enlace no cambia."
			};
			Place(f, c1, 20f, 18f, 420f, 190f);
			PaintBox status = Box(c1, 20f, 52f, 380f, 34f, delegate(Graphics g, Rectangle r)
			{
				string st = tunnel.State;
				Color c = st == "conectado" ? Theme.Green : (st.StartsWith("error") ? Theme.Red : Theme.Muted);
				string text = (st == "conectado") ? ("● Conectado: " + tunnel.Url) : ((st == "apagado") ? "○ Apagado" : ("● " + CapFirst(st)));
				Theme.DrawWrapped(g, text, Theme.BodySemi(8.25f), c, r);
			});
			Label(c1, "NGROK · AUTHTOKEN", 20f, 92f, 220f);
			VButton help = new VButton { Kind = ButtonKind.Link, Text = "Conseguir la clave →", FontSize = 8.25f };
			Place(c1, help, 230f, 89f, 170f, 22f);
			help.Click += delegate
			{
				Http.OpenBrowser("https://dashboard.ngrok.com/get-started/your-authtoken");
			};
			Field fTok = new Field();
			fTok.Box.UseSystemPasswordChar = true;
			fTok.Box.Text = ngrokToken;
			Place(c1, fTok, 20f, 110f, 380f, 34f);
			Toggle tOn = new Toggle { Text = "Activado (mientras StreamDelay esté abierto)", Checked = ngrokOn };
			Place(c1, tOn, 18f, 152f, 384f, 28f);
			Action apply = delegate
			{
				ngrokToken = fTok.Box.Text.Trim();
				ngrokOn = tOn.Checked;
				SaveConfig();
				UpdateTunnel();
			};
			tOn.CheckedChanged += delegate
			{
				apply();
			};
			fTok.Box.Leave += delegate
			{
				if (fTok.Box.Text.Trim() != ngrokToken)
				{
					apply();
				}
			};

			Card c2 = new Card
			{
				Title = "Mods",
				Subtitle = "Uno por persona. «Quitar» le corta el acceso al momento."
			};
			Place(f, c2, 20f, 218f, 420f, 316f);
			Label(c2, "NOMBRE DEL MOD", 20f, 50f, 300f);
			Field fName = new Field();
			fName.Box.MaxLength = 24;
			Place(c2, fName, 20f, 68f, 270f, 34f);
			VButton add = new VButton { Kind = ButtonKind.Primary, Text = "Añadir", FontSize = 9f };
			Place(c2, add, 298f, 68f, 102f, 34f);
			List<Control> rowControls = new List<Control>();
			Action rows = null;
			rows = delegate
			{
				foreach (Control rc in rowControls)
				{
					c2.Controls.Remove(rc);
					rc.Dispose();
				}
				rowControls.Clear();
				List<ModUser> list;
				lock (mods)
				{
					list = new List<ModUser>(mods);
				}
				for (int i = 0; i < list.Count; i++)
				{
					ModUser m = list[i];
					float y = 112f + (float)i * 32f;
					PaintBox nameBox = Box(c2, 20f, y, 170f, 28f, delegate(Graphics g, Rectangle r)
					{
						string acc = "";
						if (m.Tw != null && m.Tw.Connected)
						{
							acc += " · Twitch";
						}
						if (m.Kc != null && m.Kc.Connected)
						{
							acc += " · Kick";
						}
						if (m.Yt != null && m.Yt.Connected)
						{
							acc += " · YouTube";
						}
						Theme.DrawText(g, m.Name + acc, Theme.BodySemi(8.5f), Theme.Text, r, StringAlignment.Near, StringAlignment.Center);
					});
					VButton copy = new VButton { Kind = ButtonKind.Ghost, Text = "Copiar enlace", FontSize = 8f };
					Place(c2, copy, 196f, y, 118f, 28f);
					copy.Click += delegate
					{
						string link = ModLink(m);
						if (link.Length == 0)
						{
							I18n.Show(f, "Primero tiene que estar conectado (arriba): pega tu clave de ngrok y activa.", "Enlaces para mods");
							return;
						}
						CopyText(link, copy, "Copiar enlace");
					};
					VButton del = new VButton { Kind = ButtonKind.Ghost, Text = "Quitar", FontSize = 8f };
					Place(c2, del, 320f, y, 80f, 28f);
					del.Click += delegate
					{
						lock (mods)
						{
							mods.Remove(m);
						}
						chatServer.DropMod(m.Key);
						AppendLog("Enlace del mod " + m.Name + " quitado.");
						SaveConfig();
						UpdateTunnel();
						rows();
					};
					rowControls.Add(nameBox);
					rowControls.Add(copy);
					rowControls.Add(del);
				}
				bool full = list.Count >= 6;
				add.Enabled = !full;
				fName.Box.Enabled = !full;
			};
			add.Click += delegate
			{
				string n = fName.Box.Text.Trim();
				if (n.Length == 0)
				{
					return;
				}
				ModUser m = new ModUser { Name = n, Key = NewModKey() };
				lock (mods)
				{
					if (mods.Count >= 6)
					{
						return;
					}
					mods.Add(m);
				}
				fName.Box.Text = "";
				AppendLog("Enlace para el mod " + n + " creado.");
				SaveConfig();
				UpdateTunnel();
				rows();
			};
			Box(f, 22f, 540f, 416f, 40f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Mándale a cada mod su enlace. Moderan con tus cuentas (queda en el Registro quién hizo qué) y escriben con las suyas, que conectan desde su enlace. Solo funciona con StreamDelay abierto.", Theme.Body(7.75f), Theme.Muted, r);
			});
			VButton close = new VButton { Kind = ButtonKind.Primary, Text = "Cerrar", FontSize = 10f };
			Place(f, close, 20f, 584f, 420f, 42f);
			close.Click += delegate
			{
				if (fTok.Box.Text.Trim() != ngrokToken)
				{
					apply();
				}
				f.Close();
			};
			// El estado del túnel cambia solo (conectando → conectado): se repinta.
			Timer t = new Timer { Interval = 700 };
			t.Tick += delegate
			{
				status.Invalidate();
			};
			t.Start();
			f.FormClosed += delegate
			{
				t.Stop();
				t.Dispose();
			};
			rows();
			f.ShowDialog(this);
		}

		// ---------- Configuración ----------

		private void LoadModsConfig(Dictionary<string, string> raw)
		{
			string v;
			if (raw.TryGetValue("ngrok_token", out v))
			{
				ngrokToken = Secret.Unprotect(v);
			}
			if (raw.TryGetValue("ngrok_on", out v))
			{
				ngrokOn = v == "1";
			}
			lock (mods)
			{
				mods.Clear();
				if (!raw.TryGetValue("mods", out v) || v.Length == 0)
				{
					return;
				}
				// Una línea por mod: nombre \t clave \t Twitch \t Kick \t YouTube (todo cifrado junto).
				foreach (string line in Secret.Unprotect(v).Split('\r'))
				{
					string[] p = line.Split('\t');
					if (p.Length < 2 || p[1].Length < 16)
					{
						continue;
					}
					ModUser m = new ModUser { Name = p[0], Key = p[1] };
					if (p.Length > 2 && p[2].Length > 0)
					{
						ModTwitch(m).LoadTokens(p[2]);
					}
					if (p.Length > 3 && p[3].Length > 0)
					{
						ModKick(m).LoadTokens(p[3]);
					}
					if (p.Length > 4 && p[4].Length > 0)
					{
						ModYouTube(m).LoadTokens(p[4]);
					}
					mods.Add(m);
				}
			}
		}

		private void SaveModsConfig(List<string> lines)
		{
			lines.Add("ngrok_token=" + Secret.Protect(ngrokToken));
			lines.Add("ngrok_on=" + (ngrokOn ? "1" : "0"));
			StringBuilder sb = new StringBuilder();
			lock (mods)
			{
				foreach (ModUser m in mods)
				{
					if (sb.Length > 0)
					{
						sb.Append('\r');
					}
					sb.Append(m.Name.Replace("\t", " ").Replace("\r", " ")).Append('\t').Append(m.Key).Append('\t')
						.Append((m.Tw != null) ? m.Tw.SaveTokens() : "").Append('\t')
						.Append((m.Kc != null) ? m.Kc.SaveTokens() : "").Append('\t')
						.Append((m.Yt != null) ? m.Yt.SaveTokens() : "");
				}
			}
			lines.Add("mods=" + ((sb.Length > 0) ? Secret.Protect(sb.ToString()) : ""));
		}
	}
}
