// Licencia en la ventana: el botón del plan (arriba), la ventana "Licencia", el aviso de las
// pestañas que son del plan Completo y lo que se guarda. Las reglas están en License.cs.
//
// Qué se guarda:
//   StreamDelay.ini   lic= (la clave, cifrada), lic_act= (activación de Polar en este PC),
//                     lic_ok= (última comprobación de Polar, cifrada), t0= (inicio de la prueba)
//   Registro de Windows (HKCU\Software\Vortexs\StreamDelay): inicio de la prueba y la última vez
//                     que se abrió, para que reinstalar o borrar el .ini no dé otra prueba.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private const int PageLocked = 7;

		private const string RegPath = "Software\\Vortexs\\StreamDelay";

		// Plan con el que funciona ahora el programa.
		private Plan plan = Plan.Full;

		// De dónde sale ese plan (prueba o clave), para enseñarlo.
		private LicenseInfo planInfo = new LicenseInfo();

		// Por qué la clave guardada no vale (si no vale).
		private string licProblem = "";

		private string licKey = "";

		private string licActivation = "";

		// Polar: "plan|ticks UTC de la última comprobación buena|correo".
		private string licCache = "";

		private DateTime trialStart = DateTime.UtcNow;

		private bool clockBack;

		private bool licChecking;

		private DateTime licNextCheck = DateTime.MinValue;

		private DateTime licNextTick = DateTime.MinValue;

		// Avisos ya enseñados: "h" bienvenida, "a" queda menos de 1 día, "f" la prueba ha terminado.
		private string licSeen = "";

		private bool licNoticeOpen;

		private VButton btnPlan;

		private PaintBox lockBox;

		// Pestaña que se quería abrir cuando salió el aviso del plan Completo.
		private int lockedPage = PageChat;

		// ---------- Guardar y cargar ----------

		private void LoadLicenseConfig(Dictionary<string, string> raw)
		{
			string v;
			licKey = raw.TryGetValue("lic", out v) ? Secret.Unprotect(v) : "";
			licActivation = raw.TryGetValue("lic_act", out v) ? v.Trim() : "";
			licCache = raw.TryGetValue("lic_ok", out v) ? Secret.Unprotect(v) : "";
			licSeen = raw.TryGetValue("lic_avisos", out v) ? v.Trim() : "";
			// Inicio de la prueba: el más antiguo entre el .ini y el registro de Windows.
			DateTime now = DateTime.UtcNow;
			DateTime start = now;
			DateTime seen = DateTime.MinValue;
			DateTime t;
			if (raw.TryGetValue("t0", out v) && TryTicks(Secret.Unprotect(v), out t) && t < start)
			{
				start = t;
			}
			try
			{
				using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
				{
					if (TryTicks(Secret.Unprotect(k.GetValue("p") as string), out t) && t < start)
					{
						start = t;
					}
					if (TryTicks(Secret.Unprotect(k.GetValue("v") as string), out t))
					{
						seen = t;
					}
					k.SetValue("p", Secret.Protect(start.Ticks.ToString(CultureInfo.InvariantCulture)));
				}
			}
			catch
			{
			}
			trialStart = start;
			// Si el reloj del PC va más de un día por detrás de la última vez, la prueba no vale.
			clockBack = seen > now.AddDays(1.0);
			ComputePlan(false);
		}

		private void SaveLicenseConfig(List<string> lines)
		{
			lines.Add("lic=" + Secret.Protect(licKey));
			lines.Add("lic_act=" + licActivation);
			lines.Add("lic_ok=" + Secret.Protect(licCache));
			lines.Add("t0=" + Secret.Protect(trialStart.Ticks.ToString(CultureInfo.InvariantCulture)));
			lines.Add("lic_avisos=" + licSeen);
		}

		private static bool TryTicks(string s, out DateTime t)
		{
			long ticks;
			t = DateTime.MinValue;
			if (string.IsNullOrEmpty(s) || !long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) || ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
			{
				return false;
			}
			t = new DateTime(ticks, DateTimeKind.Utc);
			return true;
		}

		// ---------- Calcular el plan ----------

		// Lo que da la clave guardada (sin internet: las de Polar, según la última comprobación).
		private LicenseInfo KeyInfo(DateTime now)
		{
			if (licKey.Length == 0)
			{
				return null;
			}
			if (License.IsSigned(licKey))
			{
				return License.CheckSigned(licKey, now);
			}
			string[] c = licCache.Split('|');
			DateTime when;
			if (c.Length < 3 || !TryTicks(c[1], out when))
			{
				return LicenseInfo.Bad("falta comprobar la clave por internet");
			}
			LicenseInfo info = new LicenseInfo
			{
				Source = "polar",
				Name = c[2],
				Plan = (Plan)Math.Max(0, Math.Min(2, (int)(c[0][0] - '0')))
			};
			if (now - when > TimeSpan.FromDays(7.0))
			{
				info.Plan = Plan.None;
				info.Error = "hace más de 7 días que no se puede comprobar la suscripción (¿hay internet?)";
			}
			return info;
		}

		// Elige el mejor entre la prueba y la clave. Con apply, quita lo del plan Completo si ya no lo hay.
		private void ComputePlan(bool apply)
		{
			DateTime now = DateTime.UtcNow;
			LicenseInfo trial = License.Trial(trialStart, now);
			if (clockBack)
			{
				trial = LicenseInfo.Bad("la fecha del PC está atrasada");
				trial.Source = "prueba";
			}
			LicenseInfo key = KeyInfo(now);
			licProblem = (key != null) ? key.Error : "";
			LicenseInfo best = (key != null && key.Plan != Plan.None && key.Plan >= trial.Plan) ? key : trial;
			if (key != null && key.Plan == Plan.None && trial.Plan == Plan.None)
			{
				best = key;
			}
			Plan before = plan;
			plan = best.Plan;
			planInfo = best;
			try
			{
				using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
				{
					k.SetValue("v", Secret.Protect(now.Ticks.ToString(CultureInfo.InvariantCulture)));
				}
			}
			catch
			{
			}
			if (btnPlan != null)
			{
				string text = PlanButtonText();
				if (btnPlan.Text != text)
				{
					btnPlan.Text = text;
					btnPlan.Invalidate();
				}
			}
			if (!apply || before == plan)
			{
				return;
			}
			if (before == Plan.Full && plan != Plan.Full)
			{
				AppendLog(plan == Plan.Basic ? "Licencia: ahora tienes el plan Básico. Chat, Overlays, Mando y Título son del plan Completo." : "Licencia: " + (planInfo.Error.Length > 0 ? planInfo.Error : "no hay ningún plan activo") + ". Elige un plan con el botón de arriba.");
				if (chatHub != null)
				{
					StopChat();
					loading = true;
					tgChat.Checked = false;
					loading = false;
				}
			}
			else if (plan > before)
			{
				AppendLog("Licencia: plan " + License.PlanName(plan) + " activado.");
			}
			if (tabs != null)
			{
				ShowPage(TabPages[tabs.Selected]);
			}
		}

		private string PlanButtonText()
		{
			if (plan == Plan.None)
			{
				return "Activar licencia";
			}
			if (planInfo.Source == "prueba")
			{
				return "Prueba · " + License.Left(planInfo.Expires, DateTime.UtcNow);
			}
			return "Plan " + License.PlanName(plan);
		}

		// Cada medio segundo desde el reloj de la ventana: cada minuto se recalcula el plan (la prueba
		// se acaba sola) y cada 6 horas se vuelve a comprobar la suscripción de Polar.
		private void LicenseTick()
		{
			DateTime now = DateTime.UtcNow;
			if (now < licNextTick)
			{
				return;
			}
			licNextTick = now.AddMinutes(1.0);
			// Mientras se emite no se quita nada: el cambio espera a que se acabe el directo.
			if (!engine.OutputActive)
			{
				ComputePlan(true);
			}
			if (licKey.Length > 0 && !License.IsSigned(licKey) && License.PolarReady && now >= licNextCheck && !licChecking)
			{
				CheckPolar(null);
			}
			LicenseNotices();
		}

		// ---------- Avisos de la prueba ----------
		// Cada uno sale una sola vez, nunca en pleno directo, ni encima de otra ventana o aviso, ni si
		// StreamDelay no es la ventana activa: si no se puede, espera al minuto siguiente.

		private void LicenseNotices()
		{
			if (licNoticeOpen || !base.Visible || Form.ActiveForm != this || engine.OutputActive || base.OwnedForms.Length > 0 || planInfo.Source != "prueba")
			{
				return;
			}
			DateTime now = DateTime.UtcNow;
			if (plan == Plan.Full && licSeen.IndexOf('h') < 0)
			{
				MarkSeen("h");
				if (Notice("Bienvenido a StreamDelay", "3 días gratis con todo",
					"Durante 3 días puedes usar StreamDelay completo: delay dinámico, multistream, chat conjunto, overlays, Mando y título.\n\nDespués elige un plan, mes a mes y sin permanencia:\n•  Básico, " + License.PriceBasic + ": delay dinámico y multistream.\n•  Completo, " + License.PriceFull + ": todo.\n\nLos días que te quedan se ven arriba, debajo de «English». Si ya tienes una clave, pégala en «Ver planes o activar una clave».",
					"Empezar la prueba", "Ver planes o activar una clave"))
				{
					return;
				}
				LicenseDialog();
				return;
			}
			if (plan == Plan.Full && planInfo.Expires - now < TimeSpan.FromDays(1.0) && licSeen.IndexOf('a') < 0)
			{
				MarkSeen("a");
				if (!Notice("Prueba", "Te queda " + License.Left(planInfo.Expires, now) + " de prueba",
					"Cuando termine, para seguir emitiendo con delay necesitarás un plan:\n•  Básico, " + License.PriceBasic + ": delay dinámico y multistream.\n•  Completo, " + License.PriceFull + ": todo.\n\nSi te suscribes ahora no pierdes nada: el plan empieza a contar desde que pagas.",
					"Ver planes", "Ahora no"))
				{
					return;
				}
				LicenseDialog();
				return;
			}
			if (plan == Plan.None && licSeen.IndexOf('f') < 0)
			{
				MarkSeen("f");
				if (!Notice("Prueba", "La prueba de 3 días ha terminado",
					"Gracias por probar StreamDelay. Para seguir usándolo elige un plan, mes a mes y sin permanencia:\n•  Básico, " + License.PriceBasic + ": delay dinámico y multistream.\n•  Completo, " + License.PriceFull + ": todo, también chat, overlays, Mando y título.\n\nTus ajustes se quedan guardados.",
					"Ver planes", "Ahora no"))
				{
					return;
				}
				LicenseDialog();
			}
		}

		private void MarkSeen(string flag)
		{
			if (licSeen.IndexOf(flag, StringComparison.Ordinal) < 0)
			{
				licSeen += flag;
				SaveConfig();
			}
		}

		// Ventana de aviso con dos botones. Devuelve true si se pulsa el primero (el violeta).
		private bool Notice(string title, string heading, string text, string primary, string secondary)
		{
			bool first = false;
			licNoticeOpen = true;
			try
			{
				Form f = DarkForm(title, 440, 380);
				Box(f, 24f, 20f, 392f, 280f, delegate(Graphics g, Rectangle r)
				{
					Card.Glow(g, new RectangleF(-r.Width * 0.2f, -r.Height * 0.4f, r.Width * 0.9f, r.Height * 0.8f), Color.FromArgb(40, Theme.Violet));
					Theme.DrawText(g, "STREAMDELAY 4.0", Theme.Mono(7.5f), Theme.Violet, new RectangleF(0f, 0f, r.Width, S(18f)), StringAlignment.Near, StringAlignment.Center);
					Theme.DrawText(g, heading, Theme.Display(16f), Theme.Text, new RectangleF(0f, S(20f), r.Width, S(34f)), StringAlignment.Near, StringAlignment.Center);
					Theme.DrawWrapped(g, text, Theme.Body(9f), Theme.Muted, new RectangleF(0f, S(64f), r.Width, r.Height - S(64f)));
				});
				VButton b2 = new VButton { Kind = ButtonKind.Ghost, Text = secondary, FontSize = 9f };
				VButton b1 = new VButton { Kind = ButtonKind.Primary, Text = primary, FontSize = 10f };
				Place(f, b2, 24f, 312f, 210f, 44f);
				Place(f, b1, 244f, 312f, 172f, 44f);
				b1.Click += delegate
				{
					first = true;
					f.Close();
				};
				b2.Click += delegate
				{
					f.Close();
				};
				f.ShowDialog(this);
				f.Dispose();
			}
			finally
			{
				licNoticeOpen = false;
			}
			return first;
		}

		// ---------- Polar ----------

		// Comprueba la clave en Polar (en otro hilo). done(null) si vale, o el motivo si no.
		private void CheckPolar(Action<string> done)
		{
			string key = licKey;
			string act = licActivation;
			string newAct = act;
			string newCache = null;
			string problem = null;
			bool offline = false;
			licChecking = true;
			licNextCheck = DateTime.UtcNow.AddHours(6.0);
			Background(delegate
			{
				LicenseInfo info;
				try
				{
					info = PolarValidate(key, act);
				}
				catch (ApiException ex)
				{
					if (ex.Status == 404 && act.Length > 0)
					{
						// La activación de este PC se borró (por ejemplo desde el portal): se vuelve a activar.
						act = "";
						newAct = "";
						info = PolarValidate(key, "");
					}
					else
					{
						throw;
					}
				}
				if (info.NeedsActivation)
				{
					Dictionary<string, object> body = new Dictionary<string, object>
					{
						{ "key", key },
						{ "organization_id", License.PolarOrg },
						{ "label", Environment.MachineName }
					};
					Dictionary<string, object> a;
					try
					{
						a = Http.Send("POST", License.PolarApi + "/v1/customer-portal/license-keys/activate", null, null, "application/json", Http.ToJson(body));
					}
					catch (ApiException ex2)
					{
						if (ex2.Status == 403 || ex2.Status == 400)
						{
							throw new ApiException(-1, "esta licencia ya está activada en otro PC (cada licencia vale en un solo PC). Quítala de ese PC (Licencia → Quitar clave) o desde «Gestionar o cancelar mi suscripción».");
						}
						throw;
					}
					newAct = Http.Str(a, "id");
					info = PolarValidate(key, newAct);
				}
				if (info.Plan == Plan.None)
				{
					problem = info.Error;
					newCache = "";
				}
				else
				{
					newCache = (int)info.Plan + "|" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + info.Name.Replace("|", "");
				}
			}, delegate(Exception err)
			{
				licChecking = false;
				ApiException api = err as ApiException;
				if (err != null)
				{
					if (api != null && api.Status == 0)
					{
						// Sin internet: se sigue con lo último comprobado; se reintenta en 15 minutos.
						offline = true;
						licNextCheck = DateTime.UtcNow.AddMinutes(15.0);
						problem = "no se ha podido conectar con la tienda (" + err.Message + ")";
					}
					else if (api != null && api.Status == 404)
					{
						problem = "la clave no existe o la suscripción se ha cancelado";
						newCache = "";
					}
					else
					{
						problem = Why(err);
						newCache = (api != null && api.Status == -1) ? "" : null;
					}
				}
				if (key != licKey)
				{
					return;
				}
				if (!offline)
				{
					licActivation = newAct;
				}
				if (newCache != null)
				{
					licCache = newCache;
				}
				SaveConfig();
				ComputePlan(!engine.OutputActive);
				if (problem != null && done == null)
				{
					AppendLog("Licencia: " + problem + ".");
				}
				if (done != null)
				{
					done(problem);
				}
			});
		}

		private static LicenseInfo PolarValidate(string key, string activation)
		{
			Dictionary<string, object> body = new Dictionary<string, object>
			{
				{ "key", key },
				{ "organization_id", License.PolarOrg }
			};
			if (activation.Length > 0)
			{
				body["activation_id"] = activation;
			}
			Dictionary<string, object> r = Http.Send("POST", License.PolarApi + "/v1/customer-portal/license-keys/validate", null, null, "application/json", Http.ToJson(body));
			return License.FromPolar(r, DateTime.UtcNow);
		}

		// Al quitar una clave de Polar se libera este PC (si falla, no pasa nada).
		private static void PolarDeactivate(string key, string activation)
		{
			if (activation.Length == 0 || !License.PolarReady)
			{
				return;
			}
			System.Threading.ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					Dictionary<string, object> body = new Dictionary<string, object>
					{
						{ "key", key },
						{ "organization_id", License.PolarOrg },
						{ "activation_id", activation }
					};
					Http.Send("POST", License.PolarApi + "/v1/customer-portal/license-keys/deactivate", null, null, "application/json", Http.ToJson(body));
				}
				catch
				{
				}
			});
		}

		// ---------- Qué deja hacer cada plan ----------

		// Chat, Overlays, Mando y Título son del plan Completo.
		private bool PageAllowed(int p)
		{
			if (plan == Plan.Full)
			{
				return true;
			}
			return p != PageChat && p != PageOverlays && p != PageMando && p != PageTitle;
		}

		// Para Iniciar hace falta algún plan (prueba, Básico o Completo).
		private bool CanStream()
		{
			if (plan != Plan.None)
			{
				return true;
			}
			I18n.Show(this, "No hay ningún plan activo" + (planInfo.Error.Length > 0 ? ": " + planInfo.Error : "") + ".\n\nCon el plan Básico (" + License.PriceBasic + ") tienes el delay dinámico y el multistream. Con el Completo (" + License.PriceFull + "), todo.", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Information);
			LicenseDialog();
			return plan != Plan.None;
		}

		// ---------- Botón de arriba y aviso de las pestañas ----------

		private void BuildLicense(int pageTop)
		{
			btnPlan = new VButton
			{
				Kind = ButtonKind.Link,
				Text = PlanButtonText(),
				FontSize = 8f
			};
			Place(header, btnPlan, 196f, 38f, 110f, 20f);
			btnPlan.Click += delegate
			{
				LicenseDialog();
			};
			lockBox = Box(this, 20f, pageTop + 16, 420f, 330f, PaintLocked, PageLocked);
			VButton see = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Ver planes y activar",
				FontSize = 10f
			};
			// Dentro del recuadro (si fuera aparte, quedaría tapado por él).
			Place(lockBox, see, 40f, 254f, 340f, 44f);
			see.Click += delegate
			{
				LicenseDialog();
			};
		}

		private void PaintLocked(Graphics g, Rectangle r)
		{
			Card.Glow(g, new RectangleF(r.Width * 0.1f, -r.Height * 0.2f, r.Width * 0.8f, r.Height * 0.8f), Color.FromArgb(40, Theme.Violet));
			string tab = (lockedPage == PageChat) ? "Chat" : ((lockedPage == PageOverlays) ? "Overlays" : ((lockedPage == PageMando) ? "Mando" : "Título"));
			string what;
			switch (lockedPage)
			{
			case PageChat:
				what = "El chat conjunto de Twitch, Kick, YouTube y TikTok en una sola ventana y en OBS, con moderación, comandos (!delay, !clip…), follows, donaciones y estilos para la pantalla.";
				break;
			case PageOverlays:
				what = "Overlays para OBS: chat en pantalla, espectadores, avisos de subs y donaciones, y su estilo.";
				break;
			case PageMando:
				what = "Controla el delay desde el móvil, el Stream Deck o un panel de OBS.";
				break;
			default:
				what = "Cambia el título y la categoría en Twitch, Kick y YouTube a la vez, y conecta tus cuentas para escribir y moderar en el chat.";
				break;
			}
			Theme.DrawText(g, "PLAN COMPLETO", Theme.Mono(8f), Theme.Violet, new RectangleF(0f, S(40f), r.Width, S(20f)), StringAlignment.Center, StringAlignment.Center);
			Theme.DrawText(g, tab, Theme.Display(20f), Theme.Text, new RectangleF(0f, S(64f), r.Width, S(40f)), StringAlignment.Center, StringAlignment.Center);
			Theme.DrawWrapped(g, what, Theme.Body(9.5f), Theme.Muted, new RectangleF(S(40f), S(118f), r.Width - S(80f), S(70f)));
			string now = (plan == Plan.Basic) ? "Tienes el plan Básico (delay dinámico y multistream)." : ("No hay ningún plan activo" + (planInfo.Error.Length > 0 ? ": " + planInfo.Error : "") + ".");
			Theme.DrawWrapped(g, now + " El plan Completo cuesta " + License.PriceFull + " y se cancela cuando quieras.", Theme.Body(8.5f), Theme.Dim, new RectangleF(S(40f), S(196f), r.Width - S(80f), S(50f)));
		}

		// ---------- Ventana "Licencia" ----------

		private void LicenseDialog()
		{
			Form f = DarkForm("Licencia", 460, 596);
			PaintBox status = Box(f, 20f, 16f, 420f, 92f, delegate(Graphics g, Rectangle r)
			{
				Card.Glow(g, new RectangleF(-r.Width * 0.2f, -r.Height, r.Width * 0.9f, r.Height * 2.5f), Color.FromArgb(36, (plan == Plan.None) ? Theme.Yellow : Theme.Violet));
				Theme.DrawText(g, "TU PLAN", Theme.Mono(7f), Theme.Muted, new RectangleF(0f, S(4f), r.Width, S(16f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, (plan == Plan.None) ? "Sin plan" : License.PlanName(plan), Theme.Display(18f), (plan == Plan.None) ? Theme.Yellow : Theme.Text, new RectangleF(0f, S(20f), r.Width, S(34f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawWrapped(g, StatusLine(), Theme.Body(8.5f), Theme.Muted, new RectangleF(0f, S(56f), r.Width, S(36f)));
			});

			Card plans = new Card
			{
				Title = "Planes",
				Subtitle = "Pago mes a mes. Se cancela cuando quieras."
			};
			Place(f, plans, 20f, 116f, 420f, 220f);
			PlanRow(plans, 54f, "Básico", License.PriceBasic, "Delay dinámico y multistream: Twitch, Kick, YouTube, TikTok y otros a la vez, con clips.", License.BuyBasicUrl, plan == Plan.Basic && planInfo.Source != "prueba");
			PlanRow(plans, 134f, "Completo", License.PriceFull, "Todo: además, chat conjunto con moderación, overlays, Mando (móvil y Stream Deck) y título y categoría.", License.BuyFullUrl, plan == Plan.Full && planInfo.Source != "prueba");

			Card act = new Card
			{
				Title = "Activar una clave",
				Subtitle = "La de tu compra o la que te hayan regalado."
			};
			Place(f, act, 20f, 346f, 420f, 182f);
			Field fKey = new Field();
			fKey.Box.Multiline = true;
			fKey.Box.ScrollBars = ScrollBars.None;
			fKey.Box.Text = licKey;
			Place(act, fKey, 20f, 52f, 380f, 60f);
			VButton btnAct = new VButton { Kind = ButtonKind.Primary, Text = "Activar", FontSize = 10f };
			VButton btnDel = new VButton { Kind = ButtonKind.Ghost, Text = "Quitar clave", FontSize = 9f };
			Place(act, btnAct, 20f, 122f, 250f, 42f);
			Place(act, btnDel, 280f, 122f, 120f, 42f);
			btnDel.Muted = licKey.Length == 0;

			VButton manage = new VButton { Kind = ButtonKind.Link, Text = "Gestionar o cancelar mi suscripción", FontSize = 8.5f };
			Place(f, manage, 20f, 534f, 260f, 22f);
			manage.Click += delegate
			{
				Http.OpenBrowser(License.ManageUrl);
			};
			VButton close = new VButton { Kind = ButtonKind.Ghost, Text = "Cerrar", FontSize = 10f };
			Place(f, close, 320f, 538f, 120f, 42f);
			close.Click += delegate
			{
				f.Close();
			};

			btnAct.Click += delegate
			{
				string key = fKey.Box.Text.Trim();
				if (key.Length == 0)
				{
					I18n.Show(f, "Pega primero la clave.", "Licencia");
					return;
				}
				if (License.IsSigned(key))
				{
					LicenseInfo info = License.CheckSigned(key, DateTime.UtcNow);
					if (info.Plan == Plan.None)
					{
						I18n.Show(f, "Esa clave no se puede usar: " + info.Error + ".", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						return;
					}
					SetLicenseKey(key);
					status.Invalidate();
					I18n.Show(f, "Listo: plan " + License.PlanName(info.Plan) + ((info.Name.Length > 0) ? (" a nombre de " + info.Name) : "") + ((info.Expires == DateTime.MaxValue) ? ", para siempre." : (", hasta el " + info.Expires.ToLocalTime().ToString("d/M/yyyy", CultureInfo.InvariantCulture) + ".")), "Licencia");
					return;
				}
				if (!License.PolarReady)
				{
					I18n.Show(f, "Esa clave no es de StreamDelay. Las claves empiezan por SD1-: cópiala entera, sin espacios.", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return;
				}
				string oldKey = licKey;
				string oldAct = licActivation;
				string oldCache = licCache;
				SetLicenseKey(key);
				btnAct.Text = "Comprobando…";
				btnAct.Enabled = false;
				CheckPolar(delegate(string problem)
				{
					if (f.IsDisposed)
					{
						return;
					}
					btnAct.Text = "Activar";
					btnAct.Enabled = true;
					status.Invalidate();
					if (problem == null)
					{
						I18n.Show(f, "Listo: plan " + License.PlanName(plan) + " activado.", "Licencia");
						return;
					}
					// No vale: se vuelve a la clave de antes.
					licKey = oldKey;
					licActivation = oldAct;
					licCache = oldCache;
					SaveConfig();
					ComputePlan(true);
					status.Invalidate();
					I18n.Show(f, "No se ha podido activar: " + problem + ".", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				});
			};
			btnDel.Click += delegate
			{
				if (licKey.Length == 0)
				{
					return;
				}
				if (I18n.Show(f, "¿Quitar la clave de este PC? Podrás volver a pegarla (aquí o en otro PC).", "Licencia", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				{
					return;
				}
				if (!License.IsSigned(licKey))
				{
					PolarDeactivate(licKey, licActivation);
				}
				licKey = "";
				licActivation = "";
				licCache = "";
				fKey.Box.Text = "";
				btnDel.Muted = true;
				btnDel.Invalidate();
				SaveConfig();
				ComputePlan(!engine.OutputActive);
				status.Invalidate();
			};
			f.ShowDialog(this);
			f.Dispose();
		}

		private void SetLicenseKey(string key)
		{
			if (key != licKey && !License.IsSigned(licKey))
			{
				PolarDeactivate(licKey, licActivation);
			}
			licKey = key;
			licActivation = "";
			licCache = "";
			SaveConfig();
			ComputePlan(true);
		}

		private string StatusLine()
		{
			DateTime now = DateTime.UtcNow;
			string s;
			if (plan == Plan.None)
			{
				s = "No hay ningún plan activo" + ((planInfo.Error.Length > 0) ? (": " + planInfo.Error) : "") + ". Elige uno abajo o pega una clave.";
			}
			else if (planInfo.Source == "prueba")
			{
				s = "Prueba gratis con todo: queda " + License.Left(planInfo.Expires, now) + ".";
			}
			else if (planInfo.Source == "clave")
			{
				s = "Clave" + ((planInfo.Name.Length > 0) ? (" a nombre de " + planInfo.Name) : "") + ((planInfo.Expires == DateTime.MaxValue) ? ", para siempre." : (", hasta el " + planInfo.Expires.ToLocalTime().ToString("d/M/yyyy", CultureInfo.InvariantCulture) + "."));
			}
			else
			{
				s = "Suscripción mensual" + ((planInfo.Name.Length > 0) ? (" (" + planInfo.Name + ")") : "") + ". Se comprueba por internet.";
			}
			if (licProblem.Length > 0 && planInfo.Source == "prueba")
			{
				s += " La clave guardada no vale: " + licProblem + ".";
			}
			return s;
		}

		private void PlanRow(Control parent, float y, string name, string price, string what, string url, bool current)
		{
			Box(parent, 20f, y, 270f, 72f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, name, Theme.BodyBold(11f), Theme.Text, new RectangleF(0f, 0f, r.Width, S(22f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, price, Theme.Mono(8f), Theme.Violet, new RectangleF(S(90f), 0f, r.Width - S(90f), S(22f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawWrapped(g, what, Theme.Body(8f), Theme.Muted, new RectangleF(0f, S(24f), r.Width, S(48f)));
			});
			VButton buy = new VButton
			{
				Kind = current ? ButtonKind.Ghost : ButtonKind.Primary,
				Text = current ? "Tu plan" : "Suscribirme",
				FontSize = 9f
			};
			Place(parent, buy, 300f, y + 10f, 100f, 38f);
			buy.Click += delegate
			{
				Http.OpenBrowser((url.Length > 0) ? url : License.PricesUrl);
			};
		}
	}
}
