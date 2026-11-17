// Pestaña Chat: chat conjunto de Twitch, Kick y YouTube, espectadores, follows y donaciones.
// También arranca la página local (127.0.0.1:8935…) que usan el chat, los overlays de OBS y el Mando.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// ---------- Construcción de la pestaña ----------

		private void BuildChat(int y0)
		{
			// Recuadro con los canales.
			Card card = new Card
			{
				Title = "Chat conjunto",
				Subtitle = "Twitch, Kick y YouTube en un solo chat. Sin iniciar sesión."
			};
			Place(this, card, 20f, y0, 420f, 244f, PageChat);
			tgChat = new Toggle
			{
				Text = "Activado"
			};
			Place(card, tgChat, 268f, 9f, 136f, 28f);
			tgChat.CheckedChanged += delegate
			{
				if (!loading)
				{
					if (tgChat.Checked)
					{
						StartChat(true);
					}
					else
					{
						StopChat();
					}
					SaveConfig();
				}
			};
			Label(card, "TWITCH · NOMBRE DEL CANAL", 20f, 62f, 300f);
			fldChatTw = new Field();
			Place(card, fldChatTw, 20f, 80f, 380f, 34f);
			Label(card, "KICK · NOMBRE DEL CANAL", 20f, 120f, 300f);
			fldChatKick = new Field();
			Place(card, fldChatKick, 20f, 138f, 380f, 34f);
			Label(card, "YOUTUBE · @CANAL O ENLACE DEL DIRECTO", 20f, 178f, 360f);
			fldChatYt = new Field();
			Place(card, fldChatYt, 20f, 196f, 380f, 34f);
			foreach (Field field in new Field[3] { fldChatTw, fldChatKick, fldChatYt })
			{
				field.Box.TextChanged += delegate
				{
					if (!loading)
					{
						SaveConfig();
					}
				};
			}

			// Espectadores y estado de cada chat.
			chatViewers = Box(this, 20f, y0 + 254, 420f, 104f, PaintChatViewers, PageChat);

			// Botones.
			int buttonsTop = y0 + 370;
			btnChatOpen = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Abrir chat",
				FontSize = 9.5f
			};
			Place(this, btnChatOpen, 20f, buttonsTop, 136f, 40f, PageChat);
			btnChatOpen.Click += delegate
			{
				if (EnsureChatServer())
				{
					Http.OpenBrowser(chatServer.Url);
				}
			};
			btnChatDock = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "URL para OBS",
				FontSize = 9f
			};
			Place(this, btnChatDock, 162f, buttonsTop, 136f, 40f, PageChat);
			btnChatDock.Click += delegate
			{
				if (EnsureChatServer())
				{
					CopyText(chatServer.Url, btnChatDock, "URL para OBS");
				}
			};
			btnChatAlerts = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Follows y donaciones",
				FontSize = 9f
			};
			Place(this, btnChatAlerts, 20f, buttonsTop + 48, 420f, 38f, PageChat);
			btnChatAlerts.Click += delegate
			{
				AlertsDialog();
			};
			VButton btnExtras = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Avisos de delay y comandos del chat",
				FontSize = 9f
			};
			Place(this, btnExtras, 20f, buttonsTop + 92, 420f, 38f, PageChat);
			btnExtras.Click += delegate
			{
				AnnounceDialog();
			};
			string[] notes = new string[1] { "Los overlays para OBS (chat, delay, espectadores) están en la pestaña Overlays." };
			for (int i = 0; i < notes.Length; i++)
			{
				string t = notes[i];
				Box(this, 22f, buttonsTop + 136 + i * 18, 416f, 18f, delegate(Graphics g, Rectangle r)
				{
					Theme.DrawText(g, t, Theme.Body(7.75f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
				}, PageChat);
			}

			// Cada segundo: espectadores y texto del botón "Follows y donaciones".
			System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer
			{
				Interval = 1000
			};
			refreshTimer.Tick += delegate
			{
				if (chatViewers.Visible)
				{
					chatViewers.Invalidate();
					string summary = AlertsSummary();
					if (btnChatAlerts.Text != summary)
					{
						btnChatAlerts.Text = summary;
						btnChatAlerts.Invalidate();
					}
				}
			};
			refreshTimer.Start();

			// Al abrir la ventana: página local, Mando, chat (si estaba activado) y buscar actualizaciones.
			base.Shown += delegate
			{
				StartWebQuietly();
				UpdateMandoUrl();
				if (chatAutoStart)
				{
					StartChat(false);
				}
				StartUpdateCheck();
			};
			base.FormClosed += delegate
			{
				if (chatHub != null)
				{
					chatHub.Stop();
				}
				chatServer.Stop();
			};
		}

		// ---------- Página local (chat, overlays y Mando) ----------

		// Estado que lee el overlay de OBS cada medio segundo.
		private string DelayStateJson()
		{
			bool on = engine.Enabled && engine.TargetMs > 0;
			return "{\"on\":" + (on ? "true" : "false") + ",\"s\":" + engine.TargetMs / 1000 + ",\"live\":" + (engine.OutputActive ? "true" : "false") + ",\"replay\":" + (engine.Replaying ? "true" : "false") + ",\"hideOff\":" + (ovDelayHideOff ? "true" : "false") + TrPart() + ",\"k\":" + ovDelayScale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
		}

		// Transición "Activando delay" / "Delay quitado" del overlay, si hay una en marcha.
		private string TrPart()
		{
			string t = TransitionJson();
			return (t == null) ? "" : (",\"tr\":" + t);
		}

		private void CopyOverlayUrl()
		{
			if (!EnsureChatServer())
			{
				return;
			}
			string url = "http://127.0.0.1:" + chatServer.Port + "/delay";
			try
			{
				Clipboard.SetText(url);
			}
			catch
			{
				return;
			}
			overlayCopiedUntil = DateTime.Now.AddSeconds(2.0);
			tiles[2].Invalidate();
			AppendLog("Overlay de delay: " + url + " (en OBS: fuente Navegador de 1920x1080 con esa URL; sale arriba a la derecha).");
		}

		// El overlay tiene que funcionar aunque el chat esté apagado: se abre la página local al arrancar.
		// Usa el primer puerto libre entre 8935 y 8944. Si no hay ninguno, no avisa.
		private void StartWebQuietly()
		{
			if (chatServer.Running)
			{
				return;
			}
			for (int p = ChatPort; p < ChatPort + 10; p++)
			{
				try
				{
					chatServer.Log = delegate(string m)
					{
						LogLater(m);
					};
					chatServer.OnInfo = ChatSendInfo;
					chatServer.OnSend = ChatSendAll;
					chatServer.OnDelayState = DelayStateJson;
					chatServer.OnViewersState = ViewersStateJson;
					WireServer();
					chatServer.Start(p, remoteLan);
					return;
				}
				catch
				{
				}
			}
		}

		// Igual que StartWebQuietly, pero si no puede abrir la página lo dice. Devuelve true si está abierta.
		private bool EnsureChatServer()
		{
			if (chatServer.Running)
			{
				return true;
			}
			for (int p = ChatPort; p < ChatPort + 10; p++)
			{
				try
				{
					chatServer.Log = delegate(string m)
					{
						LogLater(m);
					};
					chatServer.OnInfo = ChatSendInfo;
					chatServer.OnSend = ChatSendAll;
					chatServer.OnDelayState = DelayStateJson;
					chatServer.OnViewersState = ViewersStateJson;
					WireServer();
					chatServer.Start(p, remoteLan);
					return true;
				}
				catch
				{
				}
			}
			MessageBox.Show(this, "No se pudo abrir la página del chat: los puertos " + ChatPort + "–" + (ChatPort + 9) + " están ocupados.", "Chat");
			return false;
		}

		// ---------- Escribir en los chats ----------

		// En qué plataformas se puede escribir (las conectadas en la pestaña Título).
		private Dictionary<string, object> ChatSendInfo()
		{
			Dictionary<string, object> send = new Dictionary<string, object>
			{
				{ "twitch", twitch.Connected },
				{ "kick", kick.Connected },
				{ "youtube", youtube.Connected }
			};
			return new Dictionary<string, object> { { "send", send } };
		}

		// Envía un mensaje a varios chats a la vez (un hilo por plataforma, máximo 20 s)
		// y devuelve el resultado de cada una ("ok" o el error).
		private Dictionary<string, object> ChatSendAll(string text, string[] to)
		{
			string twCh = "";
			string kcCh = "";
			Invoke((Action)delegate
			{
				twCh = ChatHub.Clean(fldChatTw.Box.Text);
				kcCh = ChatHub.Clean(fldChatKick.Box.Text);
			});
			Dictionary<string, object> result = new Dictionary<string, object>();
			List<Thread> workers = new List<Thread>();
			foreach (string target in to)
			{
				string pid = target;
				Action send = ((pid == "twitch") ? ((Action)delegate
				{
					twitch.SendChat(twCh, text);
				}) : ((pid == "kick") ? ((Action)delegate
				{
					kick.SendChat(kcCh, text);
				}) : ((pid == "youtube") ? ((Action)delegate
				{
					youtube.SendChat(text);
				}) : null)));
				Platform plat = ((pid == "twitch") ? twitch : ((pid == "kick") ? ((Platform)kick) : ((Platform)((pid == "youtube") ? youtube : null))));
				if (send == null)
				{
					continue;
				}
				Thread worker = new Thread((ThreadStart)delegate
				{
					string value;
					if (!plat.Connected)
					{
						value = "no está conectado (pestaña Título)";
					}
					else
					{
						try
						{
							send();
							value = "ok";
						}
						catch (ApiException ex)
						{
							value = ex.Message;
						}
						catch (Exception ex2)
						{
							value = ex2.Message;
						}
					}
					lock (result)
					{
						result[pid] = value;
					}
				})
				{
					IsBackground = true
				};
				workers.Add(worker);
				worker.Start();
			}
			foreach (Thread worker in workers)
			{
				worker.Join(20000);
			}
			lock (result)
			{
				foreach (string key in to)
				{
					if (!result.ContainsKey(key))
					{
						result[key] = "sin respuesta";
					}
				}
				List<string> failures = new List<string>();
				foreach (KeyValuePair<string, object> item in result)
				{
					if ((string)item.Value != "ok")
					{
						failures.Add(item.Key + ": " + item.Value);
					}
				}
				LogLater("Mensaje enviado al chat (" + string.Join(", ", to) + ")" + ((failures.Count > 0) ? (". Fallos: " + string.Join("; ", failures.ToArray())) : "."));
				return new Dictionary<string, object>(result);
			}
		}

		// ---------- Encender y apagar el chat ----------

		private void StartChat(bool userAction)
		{
			string tw = fldChatTw.Box.Text.Trim();
			string kc = fldChatKick.Box.Text.Trim();
			string yt = fldChatYt.Box.Text.Trim();
			if (tw.Length + kc.Length + yt.Length == 0)
			{
				loading = true;
				tgChat.Checked = false;
				loading = false;
				if (userAction)
				{
					MessageBox.Show(this, "Escribe al menos un canal (Twitch, Kick o YouTube) para activar el chat.", "Chat");
				}
				return;
			}
			if (!EnsureChatServer())
			{
				loading = true;
				tgChat.Checked = false;
				loading = false;
				return;
			}
			if (chatHub != null)
			{
				chatHub.Stop();
			}
			chatHub = new ChatHub
			{
				TwitchChannel = tw,
				KickChannel = kc,
				YouTubeChannel = yt,
				TwitchApi = twitch,
				StreamElementsToken = seToken,
				StreamlabsToken = slToken
			};
			chatHub.Log = delegate(string m)
			{
				LogLater(m);
			};
			chatStartedAt = DateTime.UtcNow;
			// Cada mensaje va a la página del chat y a los comandos (!delay…).
			chatHub.Item = delegate(ChatItem it)
			{
				chatServer.Send(it);
				OnChatItem(it);
			};
			chatHub.Start();
			// Con el chat encendido no se pueden cambiar los canales.
			fldChatYt.Locked = true;
			fldChatKick.Locked = true;
			fldChatTw.Locked = true;
			AppendLog("Chat conjunto activado. Página del chat: " + chatServer.Url);
		}

		private void StopChat()
		{
			if (chatHub != null)
			{
				chatHub.Stop();
			}
			chatHub = null;
			fldChatYt.Locked = false;
			fldChatKick.Locked = false;
			fldChatTw.Locked = false;
			AppendLog("Chat conjunto desactivado.");
		}

		// Recuadro de espectadores: una columna por plataforma con su número y el estado del chat, y el total.
		private void PaintChatViewers(Graphics g, Rectangle r)
		{
			RectangleF frame = new RectangleF(0.5f, 0.5f, (float)r.Width - 1.5f, (float)r.Height - 1.5f);
			using (GraphicsPath path = Theme.Round(frame, S(14f)))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(225, Theme.Surface)))
				{
					g.FillPath(brush, path);
				}
				using (Pen pen = new Pen(Theme.Border))
				{
					g.DrawPath(pen, path);
				}
			}
			ChatHub hub = chatHub;
			string[] names = new string[3] { "TWITCH", "KICK", "YOUTUBE" };
			string[] channels = new string[3]
			{
				fldChatTw.Box.Text.Trim(),
				fldChatKick.Box.Text.Trim(),
				fldChatYt.Box.Text.Trim()
			};
			// -1 = aún no se sabe, -2 = no está en directo.
			int[] viewers = new int[3]
			{
				(hub != null) ? hub.TwitchViewers : (-1),
				(hub != null) ? hub.KickViewers : (-1),
				(hub != null) ? hub.YouTubeViewers : (-1)
			};
			string[] states = new string[3]
			{
				(hub != null) ? hub.TwitchState : "",
				(hub != null) ? hub.KickState : "",
				(hub != null) ? hub.YouTubeState : ""
			};
			float colWidth = (float)r.Width / 3f;
			float top = S(26f);
			CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
			int total = 0;
			bool anyViewers = false;
			Theme.DrawText(g, "ESPECTADORES", Theme.Mono(7f), Theme.Dim, new RectangleF(S(16f), S(8f), S(200f), S(16f)), StringAlignment.Near, StringAlignment.Center);
			for (int i = 0; i < 3; i++)
			{
				float x = (float)i * colWidth;
				string count;
				string status;
				Color color;
				if (hub == null || channels[i].Length == 0)
				{
					count = "—";
					status = ((channels[i].Length == 0) ? "No configurado" : "Chat apagado");
					color = Theme.Dim;
				}
				else
				{
					count = ((viewers[i] >= 0) ? viewers[i].ToString("N0", es) : ((viewers[i] == -2) ? "Sin directo" : "—"));
					if (viewers[i] >= 0)
					{
						total += viewers[i];
						anyViewers = true;
					}
					if (states[i] == "conectado")
					{
						status = "Chat conectado";
						color = Theme.Green;
					}
					else if (states[i].StartsWith("error"))
					{
						// "error: …" → se enseña lo que va después.
						status = CapFirst(states[i].Substring(Math.Min(7, states[i].Length)));
						color = Theme.Red;
					}
					else
					{
						status = CapFirst((states[i].Length > 0) ? states[i] : "conectando") + "…";
						color = Theme.Yellow;
					}
				}
				using (SolidBrush brush2 = new SolidBrush(color))
				{
					g.FillEllipse(brush2, x + (float)S(16f), top + (float)S(9f), S(7f), S(7f));
				}
				Theme.DrawText(g, names[i], Theme.Mono(7f), Theme.Muted, new RectangleF(x + (float)S(29f), top + (float)S(3f), colWidth - (float)S(34f), S(18f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, count, Theme.BodyBold(11.5f), Theme.Text, new RectangleF(x + (float)S(15f), top + (float)S(22f), colWidth - (float)S(20f), S(26f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, status, Theme.Body(7.25f), (color == Theme.Red) ? Color.FromArgb(255, 120, 130) : Theme.Muted, new RectangleF(x + (float)S(15f), top + (float)S(50f), colWidth - (float)S(20f), S(18f)), StringAlignment.Near, StringAlignment.Center);
				if (i > 0)
				{
					using (Pen pen2 = new Pen(Theme.Border))
					{
						g.DrawLine(pen2, x, top + (float)S(6f), x, r.Height - S(12f));
					}
				}
			}
			Theme.DrawText(g, anyViewers ? ("TOTAL " + total.ToString("N0", es)) : "", Theme.Mono(7f), Theme.Violet, new RectangleF(0f, S(8f), r.Width - S(16f), S(16f)), StringAlignment.Far, StringAlignment.Center);
		}

		// "conectando" → "Conectando".
		private static string CapFirst(string s)
		{
			s = (s ?? "").Trim();
			if (s.Length != 0)
			{
				return char.ToUpperInvariant(s[0]) + s.Substring(1);
			}
			return s;
		}

		// ---------- Follows y donaciones ----------

		// Texto del botón: "Follows y donaciones · 2 activos · revisar".
		private string AlertsSummary()
		{
			ChatHub hub = chatHub;
			string[] states = ((hub == null) ? new string[0] : new string[3] { hub.FollowsState, hub.StreamElementsState, hub.StreamlabsState });
			int active = 0;
			bool problem = false;
			foreach (string state in states)
			{
				if (state == "activo" || state == "conectado")
				{
					active++;
				}
				else if (state.StartsWith("error") || state.StartsWith("falta") || state.StartsWith("token") || state.StartsWith("solo") || state.StartsWith("sesión") || state.StartsWith("Twitch"))
				{
					problem = true;
				}
			}
			if (hub == null)
			{
				// Chat apagado: cuenta lo que está configurado.
				int configured = ((seToken.Length > 0) ? 1 : 0) + ((slToken.Length > 0) ? 1 : 0) + (twitch.Connected ? 1 : 0);
				if (configured != 0)
				{
					return "Follows y donaciones · " + configured + " configurado" + ((configured == 1) ? "" : "s");
				}
				return "Follows y donaciones · sin configurar";
			}
			return "Follows y donaciones · " + active + " activo" + ((active == 1) ? "" : "s") + (problem ? " · revisar" : "");
		}

		// Ventana "Follows y donaciones": follows de Twitch y tokens de StreamElements y Streamlabs.
		private void AlertsDialog()
		{
			Form f = DarkForm("Follows y donaciones", 460, 560);
			Card twitchCard = new Card
			{
				Title = "Follows de Twitch",
				Subtitle = "Con tu cuenta de Twitch de la pestaña Título."
			};
			Place(f, twitchCard, 20f, 18f, 420f, 176f);
			PaintBox info = new PaintBox
			{
				Painter = delegate(Graphics g, Rectangle r)
				{
					ChatHub hub = chatHub;
					string s = ((!twitch.Connected) ? "Twitch no está conectado. Conéctalo para ver los follows en el chat (tienes que ser el dueño del canal o moderador)." : ((hub == null || hub.FollowsState.Length <= 0) ? ("Cuenta: " + twitch.Account + ". Se activa al encender el chat. Si conectaste Twitch antes de esta versión, vuelve a conectarlo una vez para dar el permiso de follows.") : ("Cuenta: " + twitch.Account + ". Estado: " + hub.FollowsState + ".")));
					Theme.DrawWrapped(g, s, Theme.Body(8.5f), Theme.Text, r);
				}
			};
			Place(twitchCard, info, 20f, 60f, 380f, 60f);
			VButton conn = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = (twitch.Connected ? "Volver a conectar Twitch" : "Conectar Twitch"),
				FontSize = 9f
			};
			Place(twitchCard, conn, 20f, 126f, 220f, 36f);
			conn.Click += delegate
			{
				if (twitch.Configured || SetupDialog(twitch))
				{
					SaveConfig();
					LoginDialog(twitch);
					SaveConfig();
					RefreshTitleUi();
					conn.Text = (twitch.Connected ? "Volver a conectar Twitch" : "Conectar Twitch");
					info.Invalidate();
					// Si el chat está encendido, se reinicia para usar la nueva conexión.
					if (chatHub != null && twitch.Connected)
					{
						StartChat(false);
					}
				}
			};
			Card donationsCard = new Card
			{
				Title = "Donaciones",
				Subtitle = "Pega el token del servicio que uses (o los dos)."
			};
			Place(f, donationsCard, 20f, 206f, 420f, 260f);
			Label(donationsCard, "STREAMELEMENTS · JWT TOKEN", 20f, 60f, 260f);
			VButton seHelp = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "¿Dónde está?",
				FontSize = 8.25f
			};
			Place(donationsCard, seHelp, 280f, 58f, 120f, 22f);
			seHelp.Click += delegate
			{
				Http.OpenBrowser("https://streamelements.com/dashboard/account/channels");
			};
			Field fSe = new Field();
			Place(donationsCard, fSe, 20f, 80f, 380f, 36f);
			fSe.Box.UseSystemPasswordChar = true;
			fSe.Box.Text = seToken;
			Label(donationsCard, "STREAMLABS · SOCKET API TOKEN", 20f, 126f, 260f);
			VButton slHelp = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "¿Dónde está?",
				FontSize = 8.25f
			};
			Place(donationsCard, slHelp, 280f, 124f, 120f, 22f);
			slHelp.Click += delegate
			{
				Http.OpenBrowser("https://streamlabs.com/dashboard#/settings/api-settings");
			};
			Field fSl = new Field();
			Place(donationsCard, fSl, 20f, 146f, 380f, 36f);
			fSl.Box.UseSystemPasswordChar = true;
			fSl.Box.Text = slToken;
			Box(donationsCard, 20f, 190f, 380f, 60f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "StreamElements: Cuenta → Canales → \"Mostrar secretos\" → JWT Token. Streamlabs: Ajustes → API Settings → API Tokens → \"Your Socket API Token\". Se guardan cifrados en StreamDelay.ini.", Theme.Body(7.75f), Theme.Muted, r);
			});
			VButton cancel = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Cancelar",
				FontSize = 10f
			};
			VButton save = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Guardar",
				FontSize = 10f
			};
			Place(f, cancel, 20f, 482f, 130f, 42f);
			Place(f, save, 160f, 482f, 280f, 42f);
			cancel.Click += delegate
			{
				f.Close();
			};
			save.Click += delegate
			{
				seToken = fSe.Box.Text.Trim();
				slToken = fSl.Box.Text.Trim();
				SaveConfig();
				f.Close();
				if (chatHub != null)
				{
					StartChat(false);
				}
			};
			f.ShowDialog(this);
		}

		// ---------- Configuración (StreamDelay.ini) ----------

		// Además del chat, carga lo de los otros archivos (avisos, comandos, Mando, actualizaciones).
		private void LoadChatConfig(Dictionary<string, string> raw)
		{
			string value;
			Func<string, string> get = (string k) => (!raw.TryGetValue(k, out value)) ? "" : value;
			fldChatTw.Box.Text = get("chat_tw");
			fldChatKick.Box.Text = get("chat_kick");
			fldChatYt.Box.Text = get("chat_yt");
			// Primera vez: los canales salen de las cuentas conectadas en la pestaña Título.
			if (!raw.ContainsKey("chat_tw") && twitch.Account.Length > 0)
			{
				fldChatTw.Box.Text = twitch.Account.ToLowerInvariant();
			}
			if (!raw.ContainsKey("chat_kick") && kick.Account.Length > 0)
			{
				fldChatKick.Box.Text = kick.Account;
			}
			seToken = Secret.Unprotect(get("chat_se"));
			slToken = Secret.Unprotect(get("chat_sl"));
			chatAutoStart = get("chat_on") == "1";
			tgChat.Checked = chatAutoStart;
			LoadExtrasConfig(raw);
			LoadCmdConfig(raw);
			LoadRemoteConfig(raw);
			LoadUpdateConfig(raw);
		}

		private void SaveChatConfig(List<string> lines)
		{
			if (fldChatTw != null)
			{
				lines.Add("chat_on=" + (tgChat.Checked ? "1" : "0"));
				lines.Add("chat_tw=" + fldChatTw.Box.Text.Trim());
				lines.Add("chat_kick=" + fldChatKick.Box.Text.Trim());
				lines.Add("chat_yt=" + fldChatYt.Box.Text.Trim());
				lines.Add("chat_se=" + Secret.Protect(seToken));
				lines.Add("chat_sl=" + Secret.Protect(slToken));
				SaveExtrasConfig(lines);
				SaveCmdConfig(lines);
				SaveRemoteConfig(lines);
				SaveUpdateConfig(lines);
			}
		}
	}
}
