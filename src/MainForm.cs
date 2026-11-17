using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm : Form
	{
		private class LoginUi : ILoginUi
		{
			public volatile bool Cancel;

			public Action<string, string> OnCode;

			public Action<string> OnBrowser;

			public bool Cancelled
			{
				get
				{
					return Cancel;
				}
			}

			public void ShowCode(string code, string url)
			{
				OnCode(code, url);
			}

			public void ShowBrowserWait(string url)
			{
				OnBrowser(url);
			}
		}

		private class DarkColors : ProfessionalColorTable
		{
			public override Color ToolStripDropDownBackground
			{
				get
				{
					return Theme.Elevated;
				}
			}

			public override Color MenuBorder
			{
				get
				{
					return Theme.BorderHi;
				}
			}

			public override Color MenuItemBorder
			{
				get
				{
					return Color.FromArgb(120, Theme.Violet);
				}
			}

			public override Color MenuItemSelected
			{
				get
				{
					return Color.FromArgb(52, 44, 74);
				}
			}

			public override Color ImageMarginGradientBegin
			{
				get
				{
					return Theme.Elevated;
				}
			}

			public override Color ImageMarginGradientMiddle
			{
				get
				{
					return Theme.Elevated;
				}
			}

			public override Color ImageMarginGradientEnd
			{
				get
				{
					return Theme.Elevated;
				}
			}

			public override Color SeparatorDark
			{
				get
				{
					return Theme.Border;
				}
			}

			public override Color SeparatorLight
			{
				get
				{
					return Theme.Border;
				}
			}

			public override Color CheckBackground
			{
				get
				{
					return Color.FromArgb(60, Theme.Violet);
				}
			}

			public override Color CheckSelectedBackground
			{
				get
				{
					return Color.FromArgb(90, Theme.Violet);
				}
			}

			public override Color CheckPressedBackground
			{
				get
				{
					return Color.FromArgb(90, Theme.Violet);
				}
			}
		}

		private const string TikTokName = "TikTok";

		private const string KickServer = "rtmps://fa723fc1b171.global-contribute.live-video.net/app";

		private const int PageChat = 4;

		private const int ChatPort = 8935;

		private const string Version = "3.0";

		private const int MaxDelay = 600;

		private const uint MOD_ALT = 1u;

		private const uint MOD_CTRL = 2u;

		private const uint MOD_NOREPEAT = 16384u;

		private readonly TwitchPlatform twitch = new TwitchPlatform();

		private readonly YouTubePlatform youtube = new YouTubePlatform();

		private readonly KickPlatform kick = new KickPlatform();

		private Platform[] plats;

		private Field fldTitle;

		private Field fldCat;

		private VButton btnSearch;

		private VButton btnUpdateTitle;

		private VButton btnRecent;

		private VButton[] chips = new VButton[4];

		private Toggle[] tgPlat = new Toggle[4];

		private VButton[] btnConn = new VButton[4];

		private PaintBox titleCounter;

		private readonly List<string> recentTitles = new List<string>();

		private bool ttEnabled = true;

		private bool ytManual;

		private bool busyTitle;

		private bool titleRead;

		private readonly Destination dTwitch = new Destination("twitch", "Twitch", true)
		{
			Server = "rtmp://live.twitch.tv/app",
			Enabled = true
		};

		private readonly Destination dKick = new Destination("kick", "Kick", false)
		{
			Server = "rtmps://fa723fc1b171.global-contribute.live-video.net/app"
		};

		private readonly Destination dYouTube = new Destination("youtube", "YouTube", false)
		{
			Server = "rtmp://a.rtmp.youtube.com/live2"
		};

		private readonly Destination dCustom = new Destination("custom", "Otro", false);

		private Destination[] dests;

		private readonly Toggle[] tgDest = new Toggle[4];

		private readonly VButton[] btnEditDest = new VButton[4];

		private PaintBox destStrip;

		private static readonly int[] TabPages = new int[7] { 0, 4, 5, 6, 1, 2, 3 };

		private readonly ChatServer chatServer = new ChatServer();

		private ChatHub chatHub;

		private Toggle tgChat;

		private Field fldChatTw;

		private Field fldChatKick;

		private Field fldChatYt;

		private PaintBox chatViewers;

		private VButton btnChatOpen;

		private VButton btnChatDock;


		private VButton btnChatAlerts;

		private bool chatAutoStart;

		private string seToken = "";

		private string slToken = "";

		private readonly Engine engine = new Engine();

		private readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StreamDelay.ini");

		private readonly Queue<string> pendingLog = new Queue<string>();

		private readonly List<Control>[] pages = new List<Control>[7]
		{
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>()
		};

		private Tabs tabs;

		private PaintBox header;

		private PaintBox heroBox;

		private PaintBox footer;

		private PaintBox[] tiles = new PaintBox[3];

		private Toggle tgDelay;

		private Toggle tgReplay;

		private Toggle tgTop;

		private Toggle tgLan;

		private Toggle tgFfmpeg;

		private Slider slider;

		private VButton btnApply;

		private VButton btnStart;

		private VButton btnCut;

		private VButton btnCopyObs;

		private VButton[] presets;

		private Field fldObs;

		private Field fldPort;

		private TextBox tbLog;

		private System.Windows.Forms.Timer timer;

		private Image logo;

		private Bitmap bg;

		private bool loading;

		private bool pulse;

		private string lastLine = "";

		private int port = 1935;

		private string ObsServer
		{
			get
			{
				return "rtmp://" + (tgLan.Checked ? LanIp() : "127.0.0.1") + ":" + port + "/app";
			}
		}

		private void BuildTitle(int y0)
		{
			plats = new Platform[3] { twitch, youtube, kick };
			Card card = new Card();
			card.Title = "Título y categoría";
			Card card2 = card;
			Place(this, card2, 20f, y0, 420f, 236f, 1);
			Label(card2, "TÍTULO", 20f, 44f, 120f);
			btnRecent = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "Recientes",
				FontSize = 8f
			};
			Place(card2, btnRecent, 196f, 41f, 110f, 22f);
			btnRecent.Click += delegate
			{
				ShowRecent();
			};
			titleCounter = Box(card2, 310f, 44f, 90f, 18f, delegate(Graphics g, Rectangle r)
			{
				int length = fldTitle.Box.Text.Length;
				int num3 = TitleMax();
				Theme.DrawText(g, length + "/" + num3, Theme.Mono(7f), (length > num3) ? Theme.Red : Theme.Dim, r, StringAlignment.Far, StringAlignment.Center);
			});
			fldTitle = new Field();
			Place(card2, fldTitle, 20f, 62f, 380f, 36f);
			fldTitle.Box.MaxLength = 200;
			fldTitle.Box.TextChanged += delegate
			{
				titleCounter.Invalidate();
				if (!loading)
				{
					SaveConfig();
				}
			};
			fldTitle.Box.KeyDown += delegate(object o, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					UpdateTitles();
				}
			};
			Label(card2, "CATEGORÍA O JUEGO", 20f, 106f, 200f);
			fldCat = new Field();
			Place(card2, fldCat, 20f, 124f, 292f, 36f);
			fldCat.Box.KeyDown += delegate(object o, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					SearchCategory();
				}
			};
			btnSearch = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Buscar",
				FontSize = 9f
			};
			Place(card2, btnSearch, 320f, 124f, 80f, 36f);
			btnSearch.Click += delegate
			{
				SearchCategory();
			};
			for (int num = 0; num < 4; num++)
			{
				int idx = num;
				chips[num] = new VButton
				{
					Kind = ButtonKind.Pill,
					FontSize = 7.25f
				};
				Place(card2, chips[num], 20 + num % 2 * 194, 170 + num / 2 * 30, 186f, 26f);
				chips[num].Click += delegate
				{
					ChipMenu(idx);
				};
			}
			Card card3 = new Card();
			Place(this, card3, 20f, y0 + 248, 420f, 196f, 1);
			string[] array = new string[4] { "Twitch", "YouTube", "Kick", "TikTok" };
			for (int num2 = 0; num2 < 4; num2++)
			{
				int idx2 = num2;
				tgPlat[num2] = new Toggle
				{
					Text = array[num2],
					Hint = ""
				};
				Place(card3, tgPlat[num2], 18f, 12 + num2 * 44, 270f, 42f);
				tgPlat[num2].CheckedChanged += delegate
				{
					if (idx2 < 3)
					{
						plats[idx2].Enabled = tgPlat[idx2].Checked;
					}
					else
					{
						ttEnabled = tgPlat[idx2].Checked;
					}
					RefreshTitleUi();
					if (!loading)
					{
						SaveConfig();
					}
				};
				btnConn[num2] = new VButton
				{
					Kind = ButtonKind.Ghost,
					FontSize = 8.5f
				};
				Place(card3, btnConn[num2], 296f, 14 + num2 * 44, 104f, 32f);
				btnConn[num2].Click += delegate
				{
					if (idx2 < 3)
					{
						ConnectClicked(plats[idx2]);
					}
					else
					{
						CopyText(fldTitle.Box.Text.Trim(), btnConn[3], "Copiar título");
					}
				};
			}
			btnUpdateTitle = new VButton
			{
				Kind = ButtonKind.Primary,
				FontSize = 10.5f
			};
			Place(this, btnUpdateTitle, 20f, y0 + 456, 420f, 44f, 1);
			btnUpdateTitle.Click += delegate
			{
				UpdateTitles();
			};
			tabs.SelectedChanged += delegate
			{
				if (TabPages[tabs.Selected] == 1)
				{
					RefreshTitleUi();
					ReadCurrentOnce();
				}
			};
			RefreshTitleUi();
		}

		private int TitleMax()
		{
			int num = 140;
			Platform[] array = plats;
			foreach (Platform platform in array)
			{
				if (platform.Enabled && platform.Connected)
				{
					num = Math.Min(num, platform.TitleMax);
				}
			}
			return num;
		}

		private void RefreshTitleUi()
		{
			if (plats == null || tgPlat[0] == null)
			{
				return;
			}
			for (int i = 0; i < 3; i++)
			{
				Platform platform = plats[i];
				if (tgPlat[i].Checked != platform.Enabled)
				{
					bool flag = loading;
					loading = true;
					tgPlat[i].Checked = platform.Enabled;
					loading = flag;
				}
				tgPlat[i].Hint = (platform.Connected ? ("Conectado" + ((platform.Account.Length > 0) ? (" · " + platform.Account) : "")) : (platform.Configured ? "Sin conectar" : "Sin configurar"));
				tgPlat[i].Invalidate();
				btnConn[i].Text = (platform.Connected ? "Desconectar" : "Conectar");
				btnConn[i].Kind = (platform.Connected ? ButtonKind.Ghost : ButtonKind.Primary);
				btnConn[i].Invalidate();
				string text = ((platform.Chosen != null) ? platform.Chosen.Name : (platform.Connected ? "elige categoría" : "sin conectar"));
				chips[i].Text = platform.Name.ToUpperInvariant() + " · " + text;
				chips[i].Selected = platform.Enabled && platform.Connected && platform.Chosen != null;
				chips[i].Enabled = platform.Enabled;
				chips[i].Invalidate();
			}
			if (tgPlat[3].Checked != ttEnabled)
			{
				bool flag2 = loading;
				loading = true;
				tgPlat[3].Checked = ttEnabled;
				loading = flag2;
			}
			tgPlat[3].Hint = "Manual: copia y pega en LIVE Studio";
			tgPlat[3].Invalidate();
			if (!btnConn[3].Text.StartsWith("¡"))
			{
				btnConn[3].Text = "Copiar título";
			}
			btnConn[3].Kind = ButtonKind.Ghost;
			btnConn[3].Invalidate();
			chips[3].Text = "TIKTOK · COPIAR Y PEGAR";
			chips[3].Selected = false;
			chips[3].Enabled = ttEnabled;
			chips[3].Invalidate();
			int num = 0;
			Platform[] array = plats;
			foreach (Platform platform2 in array)
			{
				if (platform2.Enabled && platform2.Connected)
				{
					num++;
				}
			}
			btnUpdateTitle.Text = (busyTitle ? "Actualizando…" : ((num != 0) ? ("Actualizar en " + num + ((num == 1) ? " plataforma" : " plataformas")) : (ttEnabled ? "Copiar título para TikTok" : "Conecta una plataforma")));
			btnUpdateTitle.Muted = busyTitle || (num == 0 && !ttEnabled);
			btnUpdateTitle.Invalidate();
			titleCounter.Invalidate();
		}

		private void Background(Action work, Action<Exception> done)
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				Exception err = null;
				try
				{
					work();
				}
				catch (Exception ex)
				{
					err = ex;
				}
				try
				{
					BeginInvoke((Action)delegate
					{
						done(err);
					});
				}
				catch
				{
				}
			});
		}

		private static string Why(Exception ex)
		{
			if (ex is OperationCanceledException)
			{
				return "cancelado";
			}
			return ex.Message;
		}

		private void ReadCurrentOnce()
		{
			if (titleRead)
			{
				return;
			}
			Platform platform = null;
			Platform[] array = plats;
			foreach (Platform platform2 in array)
			{
				if (platform2.Connected && platform2.Enabled && platform2 != youtube)
				{
					platform = platform2;
					break;
				}
			}
			if (platform != null)
			{
				titleRead = true;
				ReadCurrent(platform);
			}
		}

		private void ReadCurrent(Platform p)
		{
			string title = null;
			Category cat = null;
			Background(delegate
			{
				p.ReadCurrent(out title, out cat);
			}, delegate(Exception err)
			{
				if (err != null)
				{
					AppendLog(p.Name + ": no se pudo leer el título actual (" + Why(err) + ").");
					RefreshTitleUi();
				}
				else
				{
					if (fldTitle.Box.Text.Trim().Length == 0 && !string.IsNullOrEmpty(title))
					{
						fldTitle.Box.Text = title;
					}
					if (cat != null && p.Chosen == null)
					{
						p.Chosen = cat;
					}
					if (cat != null && fldCat.Box.Text.Trim().Length == 0 && p != youtube)
					{
						fldCat.Box.Text = cat.Name;
					}
					SaveConfig();
					RefreshTitleUi();
				}
			});
		}

		private void SearchCategory()
		{
			string q = fldCat.Box.Text.Trim();
			if (q.Length == 0)
			{
				fldCat.Box.Focus();
				return;
			}
			int pending = 0;
			Platform[] array = new Platform[2] { twitch, kick };
			foreach (Platform platform in array)
			{
				if (!platform.Enabled || !platform.Connected)
				{
					continue;
				}
				Platform plat = platform;
				List<Category> res = null;
				pending++;
				btnSearch.Text = "…";
				Background(delegate
				{
					res = plat.Search(q);
				}, delegate(Exception err)
				{
					if (err != null)
					{
						AppendLog(plat.Name + ": no se pudo buscar la categoría (" + Why(err) + ").");
					}
					else
					{
						plat.Results = res;
						plat.Chosen = Platform.Best(res, q);
						if (plat.Chosen == null)
						{
							AppendLog(plat.Name + ": no hay ninguna categoría que coincida con \"" + q + "\".");
						}
					}
					if (--pending == 0)
					{
						btnSearch.Text = "Buscar";
						SuggestYouTube(q);
						SaveConfig();
					}
					RefreshTitleUi();
				});
			}
			if (pending == 0)
			{
				SuggestYouTube(q);
				RefreshTitleUi();
				SaveConfig();
			}
		}

		private void SuggestYouTube(string q)
		{
			if (!ytManual)
			{
				string gameName = ((twitch.Chosen != null) ? twitch.Chosen.Name : ((kick.Chosen != null) ? kick.Chosen.Name : q));
				Category category = YouTubePlatform.Suggest(gameName);
				if (category != null)
				{
					youtube.Chosen = category;
				}
			}
		}

		private void ChipMenu(int i)
		{
			if (i == 3)
			{
				CopyText(fldTitle.Box.Text.Trim(), btnConn[3], "Copiar título");
				return;
			}
			Platform p = plats[i];
			if (!p.Connected)
			{
				ConnectClicked(p);
				return;
			}
			List<Category> list = ((p == youtube) ? new List<Category>(YouTubePlatform.All) : p.Results);
			ContextMenuStrip contextMenuStrip = DarkMenu();
			if (list.Count == 0)
			{
				ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem("Escribe una categoría y pulsa Buscar");
				toolStripMenuItem.Enabled = false;
				ToolStripMenuItem value = toolStripMenuItem;
				contextMenuStrip.Items.Add(value);
			}
			foreach (Category item in list)
			{
				Category cat = item;
				ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem(item.Name);
				toolStripMenuItem2.Checked = p.Chosen != null && p.Chosen.Id == item.Id;
				ToolStripMenuItem toolStripMenuItem3 = toolStripMenuItem2;
				toolStripMenuItem3.Click += delegate
				{
					p.Chosen = cat;
					if (p == youtube)
					{
						ytManual = true;
					}
					RefreshTitleUi();
					SaveConfig();
				};
				contextMenuStrip.Items.Add(toolStripMenuItem3);
			}
			if (p.Chosen != null)
			{
				contextMenuStrip.Items.Add(new ToolStripSeparator());
				ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem("No cambiar la categoría en " + p.Name);
				toolStripMenuItem4.Click += delegate
				{
					p.Chosen = null;
					if (p == youtube)
					{
						ytManual = true;
					}
					RefreshTitleUi();
					SaveConfig();
				};
				contextMenuStrip.Items.Add(toolStripMenuItem4);
			}
			contextMenuStrip.Show(chips[i], new Point(0, chips[i].Height + 2));
		}

		private void ShowRecent()
		{
			ContextMenuStrip contextMenuStrip = DarkMenu();
			if (recentTitles.Count == 0)
			{
				contextMenuStrip.Items.Add(new ToolStripMenuItem("Aún no hay títulos usados")
				{
					Enabled = false
				});
			}
			foreach (string recentTitle in recentTitles)
			{
				string title = recentTitle;
				ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem((recentTitle.Length > 60) ? (recentTitle.Substring(0, 57) + "…") : recentTitle);
				toolStripMenuItem.Click += delegate
				{
					fldTitle.Box.Text = title;
				};
				contextMenuStrip.Items.Add(toolStripMenuItem);
			}
			contextMenuStrip.Show(btnRecent, new Point(0, btnRecent.Height + 2));
		}

		private void UpdateTitles()
		{
			if (busyTitle)
			{
				return;
			}
			string text = fldTitle.Box.Text.Trim();
			List<Platform> targets = new List<Platform>();
			Platform[] array = plats;
			foreach (Platform platform in array)
			{
				if (platform.Enabled)
				{
					if (!platform.Connected)
					{
						AppendLog(platform.Name + ": sin conectar (pulsa Conectar en la pestaña Título).");
					}
					else
					{
						targets.Add(platform);
					}
				}
			}
			if (text.Length == 0 && targets.Exists((Platform p) => p.Chosen == null))
			{
				MessageBox.Show(this, "Escribe un título (o elige una categoría) antes de actualizar.", "Título vacío");
				fldTitle.Box.Focus();
				return;
			}
			if (ttEnabled && text.Length > 0)
			{
				try
				{
					Clipboard.SetText(text);
				}
				catch
				{
				}
				AppendLog("TikTok: título copiado. Pégalo en TikTok LIVE Studio (TikTok no permite cambiarlo desde otros programas).");
			}
			if (text.Length > 0)
			{
				recentTitles.Remove(text);
				recentTitles.Insert(0, text);
				while (recentTitles.Count > 10)
				{
					recentTitles.RemoveAt(recentTitles.Count - 1);
				}
				SaveConfig();
			}
			if (targets.Count == 0)
			{
				RefreshTitleUi();
				return;
			}
			busyTitle = true;
			RefreshTitleUi();
			int pending = targets.Count;
			int ok = 0;
			foreach (Platform item in targets)
			{
				Platform plat = item;
				string t = ((text.Length == 0) ? null : ((text.Length > plat.TitleMax) ? text.Substring(0, plat.TitleMax) : text));
				if (t != null && t.Length < text.Length)
				{
					AppendLog(plat.Name + ": el título se recorta a " + plat.TitleMax + " caracteres.");
				}
				Background(delegate
				{
					plat.Update(t, plat.Chosen);
				}, delegate(Exception err)
				{
					if (err == null)
					{
						ok++;
						AppendLog(plat.Name + ": título" + ((plat.Chosen != null) ? (" y categoría (" + plat.Chosen.Name + ")") : "") + " actualizados.");
					}
					else
					{
						AppendLog(plat.Name + ": no se pudo actualizar (" + Why(err) + ").");
					}
					if (--pending <= 0)
					{
						busyTitle = false;
						RefreshTitleUi();
						btnUpdateTitle.Text = ((ok == targets.Count) ? "¡Actualizado!" : "Con errores: mira el Registro");
						btnUpdateTitle.Invalidate();
						System.Windows.Forms.Timer t2 = new System.Windows.Forms.Timer
						{
							Interval = 2500
						};
						t2.Tick += delegate
						{
							t2.Dispose();
							RefreshTitleUi();
						};
						t2.Start();
					}
				});
			}
		}

		private void ConnectClicked(Platform p)
		{
			if (p.Connected)
			{
				if (MessageBox.Show(this, "¿Desconectar " + p.Name + ((p.Account.Length > 0) ? (" (" + p.Account + ")") : "") + "?\n\nTendrás que volver a autorizarlo para cambiar el título.", "Desconectar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					p.Logout();
					SaveConfig();
					RefreshTitleUi();
					AppendLog(p.Name + ": desconectado.");
				}
			}
			else if (p.Configured || SetupDialog(p))
			{
				SaveConfig();
				LoginDialog(p);
			}
		}

		private Form DarkForm(string title, int w, int h)
		{
			Form f = new Form
			{
				Text = title,
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MaximizeBox = false,
				MinimizeBox = false,
				ShowInTaskbar = false,
				StartPosition = FormStartPosition.CenterParent,
				BackColor = Theme.Bg,
				ForeColor = Theme.Text,
				Font = Theme.Body(9f),
				AutoScaleMode = AutoScaleMode.None,
				ClientSize = new Size(S(w), S(h)),
				TopMost = base.TopMost
			};
			try
			{
				f.Icon = base.Icon;
			}
			catch
			{
			}
			f.HandleCreated += delegate
			{
				Theme.DarkTitleBar(f.Handle);
			};
			return f;
		}

		private static string[] SetupSteps(Platform p)
		{
			if (p is TwitchPlatform)
			{
				return new string[3] { "1. Abre el panel de desarrolladores de Twitch (botón de abajo) e inicia sesión. Twitch exige tener activada la verificación en dos pasos.", "2. Pulsa \"Registrar tu aplicación\" y rellena: Nombre: StreamDelay-tucanal · URL de redirección OAuth: http://localhost · Categoría: Application Integration · Tipo de cliente: Público.", "3. Pulsa Crear, después Administrar, y copia aquí el \"ID de cliente\"." };
			}
			if (p is KickPlatform)
			{
				return new string[3]
				{
					"1. Abre Kick → Ajustes → Developer (botón de abajo) y pulsa Create App. Kick exige la verificación en dos pasos.",
					"2. Redirect URL: http://localhost:" + 17563 + "/callback (cópiala con el botón). Marca los permisos: Read user information, Read channel information, Write to channel information y el de escribir en el chat (chat:write, para el chat conjunto).",
					"3. Guarda la app y copia aquí el Client ID y el Client Secret."
				};
			}
			return new string[3] { "1. Abre Google Cloud (botón de abajo), crea un proyecto \"StreamDelay\" y en Biblioteca habilita \"YouTube Data API v3\".", "2. Pantalla de consentimiento / Google Auth Platform: tipo Externo, pon un nombre y tu correo. En Público, pulsa \"Publicar aplicación\" (si no, Google te desconecta cada 7 días).", "3. Credenciales → Crear credenciales → ID de cliente OAuth → tipo \"TVs y dispositivos de entrada limitada\". Copia aquí el ID de cliente y el secreto." };
		}

		private bool SetupDialog(Platform p)
		{
			Form f = DarkForm("Configurar " + p.Name, 460, p.NeedsSecret ? 548 : 470);
			string[] steps = SetupSteps(p);
			Card card = new Card();
			card.Title = "Crea tu app de " + p.Name;
			card.Subtitle = "Solo una vez. Es gratis y tarda unos minutos.";
			Card card2 = card;
			Place(f, card2, 20f, 18f, 420f, 268f);
			PaintBox paintBox = new PaintBox();
			paintBox.Painter = delegate(Graphics g, Rectangle r)
			{
				float num2 = 0f;
				string[] array = steps;
				foreach (string text in array)
				{
					SizeF sizeF = g.MeasureString(text, Theme.Body(8.5f), r.Width);
					Theme.DrawWrapped(g, text, Theme.Body(8.5f), Theme.Text, new RectangleF(0f, num2, r.Width, sizeF.Height + 2f));
					num2 += sizeF.Height + (float)S(8f);
				}
			};
			PaintBox c = paintBox;
			Place(card2, c, 20f, 62f, 380f, 160f);
			VButton vButton = new VButton();
			vButton.Kind = ButtonKind.Ghost;
			vButton.Text = "Abrir panel de " + p.Name;
			vButton.FontSize = 9f;
			VButton vButton2 = vButton;
			Place(card2, vButton2, 20f, 220f, 200f, 34f);
			vButton2.Click += delegate
			{
				Http.OpenBrowser(p.ConsoleUrl);
			};
			string redirect = ((p is KickPlatform) ? KickPlatform.RedirectUri : ((p is TwitchPlatform) ? "http://localhost" : null));
			if (redirect != null)
			{
				VButton cr = new VButton
				{
					Kind = ButtonKind.Ghost,
					Text = "Copiar URL de redirección",
					FontSize = 8.5f
				};
				Place(card2, cr, 228f, 220f, 172f, 34f);
				cr.Click += delegate
				{
					CopyText(redirect, cr, "Copiar URL de redirección");
				};
			}
			int num = 300;
			Label(f, (p is TwitchPlatform) ? "ID DE CLIENTE" : "CLIENT ID", 22f, num, 300f);
			Field id = new Field();
			Place(f, id, 20f, num + 18, 420f, 36f);
			id.Box.Text = p.ClientId;
			Field sec = null;
			num += 66;
			if (p.NeedsSecret)
			{
				Label(f, "CLIENT SECRET", 22f, num, 300f);
				sec = new Field();
				Place(f, sec, 20f, num + 18, 420f, 36f);
				sec.Box.UseSystemPasswordChar = true;
				sec.Box.Text = p.ClientSecret;
				num += 66;
			}
			Box(f, 22f, num, 416f, 34f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Se guardan cifrados en StreamDelay.ini (solo tu usuario de Windows puede leerlos). No compartas ese archivo.", Theme.Body(7.75f), Theme.Muted, r);
			});
			VButton vButton3 = new VButton();
			vButton3.Kind = ButtonKind.Primary;
			vButton3.Text = "Guardar y conectar";
			vButton3.FontSize = 10f;
			VButton vButton4 = vButton3;
			VButton vButton5 = new VButton();
			vButton5.Kind = ButtonKind.Ghost;
			vButton5.Text = "Cancelar";
			vButton5.FontSize = 10f;
			VButton vButton6 = vButton5;
			Place(f, vButton6, 20f, num + 44, 130f, 42f);
			Place(f, vButton4, 160f, num + 44, 280f, 42f);
			vButton6.Click += delegate
			{
				f.DialogResult = DialogResult.Cancel;
				f.Close();
			};
			vButton4.Click += delegate
			{
				string text = id.Box.Text.Trim();
				string text2 = ((sec != null) ? sec.Box.Text.Trim() : "");
				if (text.Length < 8 || (p.NeedsSecret && text2.Length < 8))
				{
					MessageBox.Show(f, p.NeedsSecret ? "Pega el Client ID y el Client Secret." : "Pega el ID de cliente.", "Faltan datos");
				}
				else
				{
					if (text != p.ClientId)
					{
						p.Logout();
					}
					p.ClientId = text;
					p.ClientSecret = text2;
					f.DialogResult = DialogResult.OK;
					f.Close();
				}
			};
			return f.ShowDialog(this) == DialogResult.OK;
		}

		private void LoginDialog(Platform p)
		{
			Form f = DarkForm("Conectar " + p.Name, 420, 330);
			string code = "";
			string url = "";
			string status = "Preparando…";
			bool showCode = false;
			Card card = new Card();
			card.Title = "Conectar " + p.Name;
			Card card2 = card;
			Place(f, card2, 20f, 18f, 380f, 226f);
			PaintBox body = new PaintBox();
			Place(card2, body, 20f, 50f, 340f, 166f);
			body.Painter = delegate(Graphics g, Rectangle r)
			{
				if (showCode)
				{
					Theme.DrawWrapped(g, "Se ha abierto la página de " + p.Name + " (y el código está copiado). Inicia sesión y escribe este código:", Theme.Body(8.75f), Theme.Muted, new RectangleF(0f, 0f, r.Width, S(40f)));
					Font font = Theme.Display(26f);
					SizeF sizeF = Theme.Measure(g, code, font);
					RectangleF r2 = new RectangleF(((float)r.Width - sizeF.Width) / 2f, S(54f), sizeF.Width, sizeF.Height);
					using (LinearGradientBrush brush = Theme.Brand(r2, true))
					{
						g.DrawString(code, font, brush, r2.X, r2.Y, StringFormat.GenericTypographic);
					}
					Theme.DrawText(g, status, Theme.Mono(7.5f), Theme.Dim, new RectangleF(0f, S(118f), r.Width, S(20f)), StringAlignment.Center, StringAlignment.Center);
				}
				else
				{
					Theme.DrawWrapped(g, status, Theme.Body(9f), Theme.Text, new RectangleF(0f, 0f, r.Width, r.Height));
				}
			};
			VButton openB = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Abrir página",
				FontSize = 9.5f
			};
			VButton copyB = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Copiar código",
				FontSize = 9.5f
			};
			VButton cancelB = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Cancelar",
				FontSize = 9.5f
			};
			Place(f, cancelB, 20f, 262f, 110f, 44f);
			Place(f, copyB, 138f, 262f, 126f, 44f);
			Place(f, openB, 272f, 262f, 128f, 44f);
			VButton vButton = openB;
			bool enabled = (copyB.Enabled = false);
			vButton.Enabled = enabled;
			openB.Click += delegate
			{
				if (url.Length > 0)
				{
					Http.OpenBrowser(url);
				}
			};
			copyB.Click += delegate
			{
				CopyText(code, copyB, "Copiar código");
			};
			LoginUi ui = new LoginUi();
			ui.OnCode = delegate(string c, string u)
			{
				f.BeginInvoke((Action)delegate
				{
					code = c;
					url = u;
					showCode = true;
					status = "ESPERANDO A QUE LO APRUEBES…";
					VButton vButton2 = openB;
					bool enabled2 = (copyB.Enabled = true);
					vButton2.Enabled = enabled2;
					try
					{
						Clipboard.SetText(c);
					}
					catch
					{
					}
					Http.OpenBrowser(u);
					body.Invalidate();
				});
			};
			ui.OnBrowser = delegate(string u)
			{
				f.BeginInvoke((Action)delegate
				{
					url = u;
					status = "Se ha abierto el navegador. Inicia sesión en " + p.Name + " y pulsa \"Allow\" / \"Permitir\".\n\nCuando termines, esta ventana se cerrará sola.";
					openB.Text = "Abrir de nuevo";
					openB.Enabled = true;
					copyB.Visible = false;
					body.Invalidate();
				});
			};
			cancelB.Click += delegate
			{
				ui.Cancel = true;
				f.Close();
			};
			f.FormClosing += delegate
			{
				ui.Cancel = true;
			};
			bool done = false;
			f.Shown += delegate
			{
				Background(delegate
				{
					p.Login(ui);
				}, delegate(Exception err)
				{
					done = true;
					if (err == null)
					{
						AppendLog(p.Name + ": conectado" + ((p.Account.Length > 0) ? (" como " + p.Account) : "") + ".");
						SaveConfig();
						RefreshTitleUi();
						if (!f.IsDisposed)
						{
							f.Close();
						}
						ReadCurrent(p);
					}
					else if (!(err is OperationCanceledException))
					{
						AppendLog(p.Name + ": no se pudo conectar (" + Why(err) + ").");
						if (!f.IsDisposed)
						{
							showCode = false;
							status = "No se pudo conectar: " + Why(err) + ((err is ApiException && ((ApiException)err).Status >= 400 && ((ApiException)err).Status < 500) ? ("\n\nRevisa el ID de cliente" + (p.NeedsSecret ? " y el secreto" : "") + " (Configurar de nuevo: desconecta y vuelve a conectar).") : "");
							VButton vButton2 = openB;
							bool enabled2 = (copyB.Enabled = false);
							vButton2.Enabled = enabled2;
							cancelB.Text = "Cerrar";
							body.Invalidate();
							if (Why(err).ToLowerInvariant().Contains("client"))
							{
								p.ClientId = "";
								SaveConfig();
							}
						}
					}
					RefreshTitleUi();
				});
			};
			f.ShowDialog(this);
			if (!done)
			{
				ui.Cancel = true;
			}
		}

		private ContextMenuStrip DarkMenu()
		{
			ContextMenuStrip m = new ContextMenuStrip
			{
				Renderer = new ToolStripProfessionalRenderer(new DarkColors()),
				BackColor = Theme.Elevated,
				ForeColor = Theme.Text,
				Font = Theme.Body(9f),
				ShowImageMargin = false,
				ShowCheckMargin = true
			};
			m.Closed += delegate
			{
				m.BeginInvoke((Action)delegate
				{
					m.Dispose();
				});
			};
			return m;
		}

		private void LoadTitleConfig(Dictionary<string, string> raw)
		{
			string value;
			Func<string, string> func = (string k) => (!raw.TryGetValue(k, out value)) ? "" : value;
			Platform[] array = plats;
			foreach (Platform platform in array)
			{
				platform.Load(func);
			}
			fldTitle.Box.Text = func("title");
			fldCat.Box.Text = func("cat_query");
			ttEnabled = func("tt_on") != "0";
			ytManual = func("yt_manual") == "1";
			recentTitles.Clear();
			string text = func("title_recent");
			if (text.Length > 0)
			{
				try
				{
					string[] array2 = Encoding.UTF8.GetString(Convert.FromBase64String(text)).Split('\n');
					foreach (string text2 in array2)
					{
						if (text2.Trim().Length > 0)
						{
							recentTitles.Add(text2);
						}
					}
				}
				catch
				{
				}
			}
			RefreshTitleUi();
		}

		private void SaveTitleConfig(List<string> lines)
		{
			if (plats != null && fldTitle != null)
			{
				Platform[] array = plats;
				foreach (Platform platform in array)
				{
					platform.Save(lines);
				}
				lines.Add("title=" + fldTitle.Box.Text.Replace("\r", " ").Replace("\n", " "));
				lines.Add("cat_query=" + fldCat.Box.Text.Replace("\n", " "));
				lines.Add("tt_on=" + (ttEnabled ? "1" : "0"));
				lines.Add("yt_manual=" + (ytManual ? "1" : "0"));
				lines.Add("title_recent=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", recentTitles.ToArray()))));
			}
		}

		private static string DefaultServer(Destination d)
		{
			if (d.Id == "twitch")
			{
				return "rtmp://live.twitch.tv/app";
			}
			if (d.Id == "kick")
			{
				return "rtmps://fa723fc1b171.global-contribute.live-video.net/app";
			}
			if (d.Id == "youtube")
			{
				return "rtmp://a.rtmp.youtube.com/live2";
			}
			return "";
		}

		private void InitDestinations()
		{
			if (dests == null)
			{
				dests = new Destination[4] { dTwitch, dKick, dYouTube, dCustom };
			}
		}

		private void BuildDestinations(int y0)
		{
			InitDestinations();
			Card card = new Card();
			card.Title = "Destinos";
			Card card2 = card;
			Place(this, card2, 20f, y0, 420f, 208f, 2);
			Box(card2, 160f, 14f, 240f, 24f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "Se emite a la vez en los activados", Theme.Body(7.75f), Theme.Muted, r, StringAlignment.Far, StringAlignment.Center);
			});
			for (int num = 0; num < dests.Length; num++)
			{
				int idx = num;
				tgDest[num] = new Toggle
				{
					Text = dests[num].Name,
					Hint = ""
				};
				Place(card2, tgDest[num], 18f, 42 + num * 40, 276f, 38f);
				tgDest[num].CheckedChanged += delegate
				{
					dests[idx].Enabled = tgDest[idx].Checked;
					RefreshDestinationsUi();
					if (!loading)
					{
						SaveConfig();
					}
				};
				btnEditDest[num] = new VButton
				{
					Kind = ButtonKind.Ghost,
					Text = "Editar",
					FontSize = 8.5f
				};
				Place(card2, btnEditDest[num], 300f, 45 + num * 40, 100f, 30f);
				btnEditDest[num].Click += delegate
				{
					EditDestination(dests[idx]);
				};
			}
		}

		private string DestHint(Destination d)
		{
			if (d.Server.Trim().Length == 0)
			{
				return "Sin servidor · pulsa Editar";
			}
			string text = d.Server.Trim();
			int num = text.IndexOf("://", StringComparison.Ordinal);
			if (num >= 0)
			{
				text = text.Substring(num + 3);
			}
			int num2 = text.IndexOf('/');
			if (num2 > 0)
			{
				text = text.Substring(0, num2);
			}
			if (text.Length > 26)
			{
				text = text.Substring(0, 12) + "…" + text.Substring(text.Length - 12);
			}
			string text2 = ((d.Key.Trim().Length > 0) ? "clave guardada" : (d.IsTwitch ? "clave: la pone OBS" : "falta la clave"));
			return text + " · " + text2;
		}

		private void RefreshDestinationsUi()
		{
			if (dests == null || tgDest[0] == null)
			{
				return;
			}
			bool running = engine.Running;
			for (int i = 0; i < dests.Length; i++)
			{
				Destination destination = dests[i];
				if (tgDest[i].Checked != destination.Enabled)
				{
					bool flag = loading;
					loading = true;
					tgDest[i].Checked = destination.Enabled;
					loading = flag;
				}
				tgDest[i].Hint = DestHint(destination);
				tgDest[i].Enabled = !running;
				tgDest[i].Invalidate();
				btnEditDest[i].Enabled = !running;
				btnEditDest[i].Invalidate();
			}
			if (destStrip != null)
			{
				destStrip.Invalidate();
			}
		}

		private List<Destination> EnabledDestinations()
		{
			List<Destination> list = new List<Destination>();
			Destination[] array = dests;
			foreach (Destination destination in array)
			{
				if (destination.Enabled)
				{
					list.Add(destination);
				}
			}
			return list;
		}

		private string DestSummary()
		{
			List<string> list = new List<string>();
			foreach (Destination item in EnabledDestinations())
			{
				list.Add(item.Name);
			}
			return string.Join(" + ", list.ToArray());
		}

		private bool ValidateDestinations()
		{
			List<Destination> list = EnabledDestinations();
			if (list.Count == 0)
			{
				GoPage(2);
				MessageBox.Show(this, "Activa al menos un destino en Ajustes → Destinos.", "Sin destinos");
				return false;
			}
			List<string> list2 = new List<string>();
			foreach (Destination item in list)
			{
				string text = item.Server.Trim();
				if (text.Length == 0 || (!text.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase)))
				{
					GoPage(2);
					MessageBox.Show(this, item.Name + ": el servidor debe empezar por rtmp:// o rtmps://", "Servidor no válido");
					return false;
				}
				if (text.Contains("127.0.0.1") || text.ToLowerInvariant().Contains("localhost"))
				{
					GoPage(2);
					MessageBox.Show(this, item.Name + ": en Destinos va el servidor de la plataforma, no la dirección local.\n\nLa dirección local (127.0.0.1) es la que se pone en OBS.", "Servidor no válido");
					return false;
				}
				if (!item.IsTwitch && item.Key.Trim().Length == 0)
				{
					list2.Add(item.Name);
				}
			}
			if (list2.Count > 0 && MessageBox.Show(this, string.Join(", ", list2.ToArray()) + ((list2.Count == 1) ? " no tiene" : " no tienen") + " clave guardada, así que no se emitirá ahí (salvo que OBS mande su clave).\n\n¿Iniciar igualmente?", "Falta la clave", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				GoPage(2);
				return false;
			}
			return true;
		}

		private void EditDestination(Destination d)
		{
			Form f = DarkForm("Destino: " + d.Name, 440, 392);
			string note = (d.IsTwitch ? "Con la Transmisión mejorada de OBS (servicio Twitch con servidor personalizado) la clave la pone OBS y puedes dejarla vacía." : ((d.Id == "kick") ? "Copia el \"Stream URL\" y la \"Stream Key\" de tu panel de Kick (Settings → Stream URL & Key)." : ((d.Id == "youtube") ? "Copia la clave de YouTube Studio → Emitir en directo → Configuración de la emisión." : "Cualquier plataforma que admita RTMP: copia su URL de servidor y su clave.")));
			Card card = new Card();
			card.Title = d.Name;
			card.Subtitle = (d.IsTwitch ? "Recibe todas las calidades de OBS" : "Recibe la calidad principal y crea el resto");
			Card card2 = card;
			Place(f, card2, 20f, 18f, 400f, 292f);
			Label(card2, "SERVIDOR", 20f, 64f, 200f);
			VButton vButton = new VButton();
			vButton.Kind = ButtonKind.Link;
			vButton.Text = "Por defecto";
			vButton.FontSize = 8f;
			VButton vButton2 = vButton;
			if (DefaultServer(d).Length > 0)
			{
				Place(card2, vButton2, 260f, 60f, 120f, 22f);
			}
			Field srv = new Field();
			Place(card2, srv, 20f, 82f, 360f, 36f);
			srv.Box.Text = d.Server;
			vButton2.Click += delegate
			{
				srv.Box.Text = DefaultServer(d);
			};
			Label(card2, "CLAVE DE TRANSMISIÓN", 20f, 126f, 200f);
			VButton show = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "Mostrar",
				FontSize = 8f
			};
			Place(card2, show, 260f, 122f, 120f, 22f);
			Field key = new Field();
			Place(card2, key, 20f, 144f, 360f, 36f);
			key.Box.UseSystemPasswordChar = true;
			key.Box.Text = d.Key;
			show.Click += delegate
			{
				key.Box.UseSystemPasswordChar = !key.Box.UseSystemPasswordChar;
				show.Text = (key.Box.UseSystemPasswordChar ? "Mostrar" : "Ocultar");
			};
			Box(card2, 20f, 190f, 360f, 90f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, note + " La clave se guarda cifrada en StreamDelay.ini.", Theme.Body(8f), Theme.Muted, r);
			});
			VButton vButton3 = new VButton();
			vButton3.Kind = ButtonKind.Ghost;
			vButton3.Text = "Cancelar";
			vButton3.FontSize = 10f;
			VButton vButton4 = vButton3;
			VButton vButton5 = new VButton();
			vButton5.Kind = ButtonKind.Primary;
			vButton5.Text = "Guardar";
			vButton5.FontSize = 10f;
			VButton vButton6 = vButton5;
			Place(f, vButton4, 20f, 326f, 130f, 44f);
			Place(f, vButton6, 160f, 326f, 260f, 44f);
			vButton4.Click += delegate
			{
				f.Close();
			};
			vButton6.Click += delegate
			{
				string text = srv.Box.Text.Trim().TrimEnd('/');
				if (text.Length > 0 && !text.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
				{
					MessageBox.Show(f, "El servidor debe empezar por rtmp:// o rtmps://", "Servidor no válido");
				}
				else
				{
					d.Server = text;
					d.Key = key.Box.Text.Trim();
					if (text.Length > 0 && (d.Key.Length > 0 || d.IsTwitch))
					{
						d.Enabled = true;
					}
					SaveConfig();
					RefreshDestinationsUi();
					f.Close();
				}
			};
			f.ShowDialog(this);
		}

		private void PaintDestStrip(Graphics g, Rectangle r)
		{
			RectangleF r2 = new RectangleF(0.5f, 0.5f, (float)r.Width - 1.5f, (float)r.Height - 1.5f);
			using (GraphicsPath path = Theme.Round(r2, S(14f)))
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
			bool running = engine.Running;
			List<Destination> list = EnabledDestinations();
			int liveCount = engine.LiveCount;
			Theme.DrawText(g, running ? "EMITIENDO EN" : "SE EMITIRÁ EN", Theme.Mono(7f), Theme.Muted, new RectangleF(S(16f), S(6f), S(200f), S(20f)), StringAlignment.Near, StringAlignment.Center);
			string s = ((!running) ? (list.Count + ((list.Count == 1) ? " DESTINO" : " DESTINOS")) : ((liveCount > 0) ? (liveCount + " EN DIRECTO") : "AÚN SIN DIRECTO"));
			Theme.DrawText(g, s, Theme.Mono(7f), (liveCount > 0) ? Theme.Mix(Theme.Red, Color.White, 0.3f) : Theme.Dim, new RectangleF(S(200f), S(6f), r.Width - S(216f), S(20f)), StringAlignment.Far, StringAlignment.Center);
			if (list.Count == 0)
			{
				Theme.DrawText(g, "Ningún destino activo · actívalos en Ajustes", Theme.Body(8.5f), Theme.Yellow, new RectangleF(S(16f), S(28f), r.Width - S(32f), S(36f)), StringAlignment.Center, StringAlignment.Center);
				return;
			}
			float num = S(8f);
			float num2 = S(12f);
			float y = S(28f);
			float height = r.Height - S(28f) - S(10f);
			float num3 = ((float)(r.Width - 2 * S(12f)) - num * (float)(list.Count - 1)) / (float)list.Count;
			foreach (Destination item in list)
			{
				Sink sink = null;
				if (running)
				{
					foreach (Sink sink2 in engine.Sinks)
					{
						if (sink2.D == item)
						{
							sink = sink2;
						}
					}
				}
				bool flag = true;
				string s2;
				Color color;
				if (!running || sink == null)
				{
					s2 = "PREPARADO";
					color = Theme.Violet;
					flag = false;
				}
				else
				{
					switch (sink.State)
					{
					case SinkState.Live:
						s2 = (engine.Replaying ? "REPITIENDO" : ((sink.Tracks > 1) ? ("EN DIRECTO · " + sink.Tracks + " CAL.") : ((sink.Quality.Length > 0) ? ("EN DIRECTO · " + sink.Quality.ToUpperInvariant()) : "EN DIRECTO")));
						color = Theme.Red;
						break;
					case SinkState.Connecting:
						s2 = "CONECTANDO";
						color = Theme.Yellow;
						break;
					case SinkState.Retrying:
						s2 = "REINTENTANDO";
						color = Color.FromArgb(255, 150, 60);
						break;
					case SinkState.NoKey:
						s2 = ((sink.Note.Length > 0) ? sink.Note.ToUpperInvariant() : "FALTA CLAVE");
						color = Theme.Red;
						flag = false;
						break;
					default:
						s2 = (engine.InputActive ? "CARGANDO DELAY" : "ESPERANDO A OBS");
						color = Theme.Violet;
						flag = false;
						break;
					}
				}
				RectangleF r3 = new RectangleF(num2, y, num3, height);
				bool flag2 = sink != null && sink.State == SinkState.Live;
				using (GraphicsPath path2 = Theme.Round(r3, S(10f)))
				{
					using (SolidBrush brush2 = new SolidBrush(flag2 ? Color.FromArgb(26, Theme.Red) : Theme.Input))
					{
						g.FillPath(brush2, path2);
					}
					using (Pen pen2 = new Pen(flag2 ? Color.FromArgb(150, Theme.Red) : Theme.Border))
					{
						g.DrawPath(pen2, path2);
					}
				}
				float num4 = S(7f);
				RectangleF rect = new RectangleF(r3.X + (float)S(10f), r3.Y + (float)S(9f), num4, num4);
				if (flag)
				{
					if (flag2 && pulse)
					{
						Card.Glow(g, RectangleF.Inflate(rect, num4, num4), Color.FromArgb(110, color));
					}
					using (SolidBrush brush3 = new SolidBrush(color))
					{
						g.FillEllipse(brush3, rect);
					}
				}
				else
				{
					using (Pen pen3 = new Pen(color, 1.4f))
					{
						g.DrawEllipse(pen3, rect);
					}
				}
				Theme.DrawText(g, item.Name, Theme.BodyBold(9f), Theme.Text, new RectangleF(rect.Right + (float)S(6f), r3.Y + (float)S(2f), r3.Width - (float)S(28f), S(20f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, s2, Theme.Mono(6.5f), flag ? Theme.Mix(color, Color.White, 0.25f) : Theme.Muted, new RectangleF(r3.X + (float)S(10f), r3.Y + (float)S(21f), r3.Width - (float)S(14f), S(14f)), StringAlignment.Near, StringAlignment.Center);
				num2 += num3 + num;
			}
		}

		private void LoadDestinations(Dictionary<string, string> raw)
		{
			InitDestinations();
			string value;
			Func<string, string> func = (string k) => (!raw.TryGetValue(k, out value)) ? null : value;
			bool flag = false;
			Destination[] array = dests;
			foreach (Destination destination in array)
			{
				string text = func("d_" + destination.Id + "_on");
				if (text != null)
				{
					flag = true;
					destination.Enabled = text == "1";
					destination.Server = func("d_" + destination.Id + "_server") ?? DefaultServer(destination);
					destination.Key = Secret.Unprotect(func("d_" + destination.Id + "_key") ?? "");
				}
			}
			if (!flag)
			{
				string text2 = func("server");
				string text3 = func("key");
				if (!string.IsNullOrEmpty(text2))
				{
					string text4 = text2.ToLowerInvariant();
					Destination destination2 = ((text4.Contains("fa723fc1b171") || text4.Contains("kick")) ? dKick : (text4.Contains("youtube") ? dYouTube : ((text4.Contains("twitch") || text4.Contains("contribute.live-video.net")) ? dTwitch : dCustom)));
					Destination[] array2 = dests;
					foreach (Destination destination3 in array2)
					{
						destination3.Enabled = false;
					}
					destination2.Enabled = true;
					destination2.Server = text2.Trim();
					destination2.Key = Secret.Unprotect(text3 ?? "");
				}
			}
			RefreshDestinationsUi();
		}

		private void SaveDestinations(List<string> lines)
		{
			if (dests != null)
			{
				Destination[] array = dests;
				foreach (Destination destination in array)
				{
					lines.Add("d_" + destination.Id + "_on=" + (destination.Enabled ? "1" : "0"));
					lines.Add("d_" + destination.Id + "_server=" + destination.Server.Trim());
					lines.Add("d_" + destination.Id + "_key=" + Secret.Protect(destination.Key));
				}
			}
		}

		private int PageTab(int page)
		{
			return Array.IndexOf(TabPages, page);
		}

		private void GoPage(int page)
		{
			tabs.Selected = PageTab(page);
		}

		private void BuildChat(int y0)
		{
			Card card = new Card();
			card.Title = "Chat conjunto";
			card.Subtitle = "Twitch, Kick y YouTube en un solo chat. Sin iniciar sesión.";
			Card card2 = card;
			Place(this, card2, 20f, y0, 420f, 244f, 4);
			tgChat = new Toggle
			{
				Text = "Activado"
			};
			Place(card2, tgChat, 268f, 9f, 136f, 28f);
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
			Label(card2, "TWITCH · NOMBRE DEL CANAL", 20f, 62f, 300f);
			fldChatTw = new Field();
			Place(card2, fldChatTw, 20f, 80f, 380f, 34f);
			Label(card2, "KICK · NOMBRE DEL CANAL", 20f, 120f, 300f);
			fldChatKick = new Field();
			Place(card2, fldChatKick, 20f, 138f, 380f, 34f);
			Label(card2, "YOUTUBE · @CANAL O ENLACE DEL DIRECTO", 20f, 178f, 360f);
			fldChatYt = new Field();
			Place(card2, fldChatYt, 20f, 196f, 380f, 34f);
			Field[] array = new Field[3] { fldChatTw, fldChatKick, fldChatYt };
			foreach (Field field in array)
			{
				field.Box.TextChanged += delegate
				{
					if (!loading)
					{
						SaveConfig();
					}
				};
			}
			chatViewers = Box(this, 20f, y0 + 254, 420f, 104f, PaintChatViewers, 4);
			int num2 = y0 + 370;
			btnChatOpen = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Abrir chat",
				FontSize = 9.5f
			};
			Place(this, btnChatOpen, 20f, num2, 136f, 40f, 4);
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
			Place(this, btnChatDock, 162f, num2, 136f, 40f, 4);
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
			Place(this, btnChatAlerts, 20f, num2 + 48, 420f, 38f, 4);
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
			Place(this, btnExtras, 20f, num2 + 92, 420f, 38f, 4);
			btnExtras.Click += delegate
			{
				AnnounceDialog();
			};
			string[] array2 = new string[1] { "Los overlays para OBS (chat, delay, espectadores) están en la pestaña Overlays." };
			for (int num3 = 0; num3 < array2.Length; num3++)
			{
				string t = array2[num3];
				Box(this, 22f, num2 + 136 + num3 * 18, 416f, 18f, delegate(Graphics g, Rectangle r)
				{
					Theme.DrawText(g, t, Theme.Body(7.75f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
				}, 4);
			}
			System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
			timer.Interval = 1000;
			System.Windows.Forms.Timer timer2 = timer;
			timer2.Tick += delegate
			{
				if (chatViewers.Visible)
				{
					chatViewers.Invalidate();
					string text = AlertsSummary();
					if (btnChatAlerts.Text != text)
					{
						btnChatAlerts.Text = text;
						btnChatAlerts.Invalidate();
					}
				}
			};
			timer2.Start();
			base.Shown += delegate
			{
				StartWebQuietly();
				UpdateMandoUrl();
				if (chatAutoStart)
				{
					StartChat(false);
				}
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

		private DateTime overlayCopiedUntil = DateTime.MinValue;

		// Estado que lee el overlay de OBS cada medio segundo.
		private string DelayStateJson()
		{
			bool on = engine.Enabled && engine.TargetMs > 0;
			return "{\"on\":" + (on ? "true" : "false") + ",\"s\":" + engine.TargetMs / 1000 + ",\"live\":" + (engine.OutputActive ? "true" : "false") + ",\"replay\":" + (engine.Replaying ? "true" : "false") + ",\"hideOff\":" + (ovDelayHideOff ? "true" : "false") + TrPart() + ",\"k\":" + ovDelayScale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
		}

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
		private void StartWebQuietly()
		{
			if (chatServer.Running)
			{
				return;
			}
			for (int i = 8935; i < 8945; i++)
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
					chatServer.Start(i, remoteLan);
					return;
				}
				catch
				{
				}
			}
		}

		private bool EnsureChatServer()
		{
			if (chatServer.Running)
			{
				return true;
			}
			for (int i = 8935; i < 8945; i++)
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
					chatServer.Start(i, remoteLan);
					return true;
				}
				catch
				{
				}
			}
			MessageBox.Show(this, "No se pudo abrir la página del chat: los puertos " + 8935 + "–" + 8944 + " están ocupados.", "Chat");
			return false;
		}

		private void LogLater(string m)
		{
			lock (pendingLog)
			{
				pendingLog.Enqueue(DateTime.Now.ToString("HH:mm:ss ") + m);
			}
		}

		private Dictionary<string, object> ChatSendInfo()
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary.Add("twitch", twitch.Connected);
			dictionary.Add("kick", kick.Connected);
			dictionary.Add("youtube", youtube.Connected);
			Dictionary<string, object> value = dictionary;
			Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
			dictionary2.Add("send", value);
			return dictionary2;
		}

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
			List<Thread> list = new List<Thread>();
			foreach (string text2 in to)
			{
				string pid = text2;
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
				Thread thread = new Thread((ThreadStart)delegate
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
				});
				thread.IsBackground = true;
				Thread thread2 = thread;
				list.Add(thread2);
				thread2.Start();
			}
			foreach (Thread item in list)
			{
				item.Join(20000);
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
				List<string> list2 = new List<string>();
				foreach (KeyValuePair<string, object> item2 in result)
				{
					if ((string)item2.Value != "ok")
					{
						list2.Add(item2.Key + ": " + item2.Value);
					}
				}
				LogLater("Mensaje enviado al chat (" + string.Join(", ", to) + ")" + ((list2.Count > 0) ? (". Fallos: " + string.Join("; ", list2.ToArray())) : "."));
				return new Dictionary<string, object>(result);
			}
		}

		private void StartChat(bool userAction)
		{
			string text = fldChatTw.Box.Text.Trim();
			string text2 = fldChatKick.Box.Text.Trim();
			string text3 = fldChatYt.Box.Text.Trim();
			if (text.Length + text2.Length + text3.Length == 0)
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
				TwitchChannel = text,
				KickChannel = text2,
				YouTubeChannel = text3,
				TwitchApi = twitch,
				StreamElementsToken = seToken,
				StreamlabsToken = slToken
			};
			chatHub.Log = delegate(string m)
			{
				LogLater(m);
			};
			chatStartedAt = DateTime.UtcNow;
			chatHub.Item = delegate(ChatItem it)
			{
				chatServer.Send(it);
				OnChatItem(it);
			};
			chatHub.Start();
			Field field = fldChatTw;
			Field field2 = fldChatKick;
			bool flag = (fldChatYt.Locked = true);
			bool locked = (field2.Locked = flag);
			field.Locked = locked;
			AppendLog("Chat conjunto activado. Página del chat: " + chatServer.Url);
		}

		private void StopChat()
		{
			if (chatHub != null)
			{
				chatHub.Stop();
			}
			chatHub = null;
			Field field = fldChatTw;
			Field field2 = fldChatKick;
			bool flag = (fldChatYt.Locked = false);
			bool locked = (field2.Locked = flag);
			field.Locked = locked;
			AppendLog("Chat conjunto desactivado.");
		}

		private void PaintChatViewers(Graphics g, Rectangle r)
		{
			RectangleF r2 = new RectangleF(0.5f, 0.5f, (float)r.Width - 1.5f, (float)r.Height - 1.5f);
			using (GraphicsPath path = Theme.Round(r2, S(14f)))
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
			ChatHub chatHub = this.chatHub;
			string[] array = new string[3] { "TWITCH", "KICK", "YOUTUBE" };
			string[] array2 = new string[3]
			{
				fldChatTw.Box.Text.Trim(),
				fldChatKick.Box.Text.Trim(),
				fldChatYt.Box.Text.Trim()
			};
			int[] array3 = new int[3]
			{
				(chatHub != null) ? chatHub.TwitchViewers : (-1),
				(chatHub != null) ? chatHub.KickViewers : (-1),
				(chatHub != null) ? chatHub.YouTubeViewers : (-1)
			};
			string[] array4 = new string[3]
			{
				(chatHub != null) ? chatHub.TwitchState : "",
				(chatHub != null) ? chatHub.KickState : "",
				(chatHub != null) ? chatHub.YouTubeState : ""
			};
			float num = (float)r.Width / 3f;
			float num2 = S(26f);
			CultureInfo cultureInfo = CultureInfo.GetCultureInfo("es-ES");
			int num3 = 0;
			bool flag = false;
			Theme.DrawText(g, "ESPECTADORES", Theme.Mono(7f), Theme.Dim, new RectangleF(S(16f), S(8f), S(200f), S(16f)), StringAlignment.Near, StringAlignment.Center);
			for (int i = 0; i < 3; i++)
			{
				float num4 = (float)i * num;
				string s;
				string s2;
				Color color;
				if (chatHub == null || array2[i].Length == 0)
				{
					s = "—";
					s2 = ((array2[i].Length == 0) ? "No configurado" : "Chat apagado");
					color = Theme.Dim;
				}
				else
				{
					s = ((array3[i] >= 0) ? array3[i].ToString("N0", cultureInfo) : ((array3[i] == -2) ? "Sin directo" : "—"));
					if (array3[i] >= 0)
					{
						num3 += array3[i];
						flag = true;
					}
					if (array4[i] == "conectado")
					{
						s2 = "Chat conectado";
						color = Theme.Green;
					}
					else if (array4[i].StartsWith("error"))
					{
						s2 = CapFirst(array4[i].Substring(Math.Min(7, array4[i].Length)));
						color = Theme.Red;
					}
					else
					{
						s2 = CapFirst((array4[i].Length > 0) ? array4[i] : "conectando") + "…";
						color = Theme.Yellow;
					}
				}
				using (SolidBrush brush2 = new SolidBrush(color))
				{
					g.FillEllipse(brush2, num4 + (float)S(16f), num2 + (float)S(9f), S(7f), S(7f));
				}
				Theme.DrawText(g, array[i], Theme.Mono(7f), Theme.Muted, new RectangleF(num4 + (float)S(29f), num2 + (float)S(3f), num - (float)S(34f), S(18f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, s, Theme.BodyBold(11.5f), Theme.Text, new RectangleF(num4 + (float)S(15f), num2 + (float)S(22f), num - (float)S(20f), S(26f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, s2, Theme.Body(7.25f), (color == Theme.Red) ? Color.FromArgb(255, 120, 130) : Theme.Muted, new RectangleF(num4 + (float)S(15f), num2 + (float)S(50f), num - (float)S(20f), S(18f)), StringAlignment.Near, StringAlignment.Center);
				if (i > 0)
				{
					using (Pen pen2 = new Pen(Theme.Border))
					{
						g.DrawLine(pen2, num4, num2 + (float)S(6f), num4, r.Height - S(12f));
					}
				}
			}
			Theme.DrawText(g, flag ? ("TOTAL " + num3.ToString("N0", cultureInfo)) : "", Theme.Mono(7f), Theme.Violet, new RectangleF(0f, S(8f), r.Width - S(16f), S(16f)), StringAlignment.Far, StringAlignment.Center);
		}

		private static string CapFirst(string s)
		{
			s = (s ?? "").Trim();
			if (s.Length != 0)
			{
				return char.ToUpperInvariant(s[0]) + s.Substring(1);
			}
			return s;
		}

		private void LoadChatConfig(Dictionary<string, string> raw)
		{
			string value;
			Func<string, string> func = (string k) => (!raw.TryGetValue(k, out value)) ? "" : value;
			fldChatTw.Box.Text = func("chat_tw");
			fldChatKick.Box.Text = func("chat_kick");
			fldChatYt.Box.Text = func("chat_yt");
			if (!raw.ContainsKey("chat_tw") && twitch.Account.Length > 0)
			{
				fldChatTw.Box.Text = twitch.Account.ToLowerInvariant();
			}
			if (!raw.ContainsKey("chat_kick") && kick.Account.Length > 0)
			{
				fldChatKick.Box.Text = kick.Account;
			}
			seToken = Secret.Unprotect(func("chat_se"));
			slToken = Secret.Unprotect(func("chat_sl"));
			chatAutoStart = func("chat_on") == "1";
			tgChat.Checked = chatAutoStart;
			LoadExtrasConfig(raw);
			LoadCmdConfig(raw);
			LoadRemoteConfig(raw);
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
			}
		}

		private string AlertsSummary()
		{
			ChatHub chatHub = this.chatHub;
			string[] array = ((chatHub == null) ? new string[0] : new string[3] { chatHub.FollowsState, chatHub.StreamElementsState, chatHub.StreamlabsState });
			int num = 0;
			bool flag = false;
			string[] array2 = array;
			foreach (string text in array2)
			{
				if (text == "activo" || text == "conectado")
				{
					num++;
				}
				else if (text.StartsWith("error") || text.StartsWith("falta") || text.StartsWith("token") || text.StartsWith("solo") || text.StartsWith("sesión") || text.StartsWith("Twitch"))
				{
					flag = true;
				}
			}
			if (chatHub == null)
			{
				int num2 = ((seToken.Length > 0) ? 1 : 0) + ((slToken.Length > 0) ? 1 : 0) + (twitch.Connected ? 1 : 0);
				if (num2 != 0)
				{
					return "Follows y donaciones · " + num2 + " configurado" + ((num2 == 1) ? "" : "s");
				}
				return "Follows y donaciones · sin configurar";
			}
			return "Follows y donaciones · " + num + " activo" + ((num == 1) ? "" : "s") + (flag ? " · revisar" : "");
		}

		private void AlertsDialog()
		{
			Form f = DarkForm("Follows y donaciones", 460, 560);
			Card card = new Card();
			card.Title = "Follows de Twitch";
			card.Subtitle = "Con tu cuenta de Twitch de la pestaña Título.";
			Card card2 = card;
			Place(f, card2, 20f, 18f, 420f, 176f);
			PaintBox info = new PaintBox
			{
				Painter = delegate(Graphics g, Rectangle r)
				{
					ChatHub chatHub = this.chatHub;
					string s = ((!twitch.Connected) ? "Twitch no está conectado. Conéctalo para ver los follows en el chat (tienes que ser el dueño del canal o moderador)." : ((chatHub == null || chatHub.FollowsState.Length <= 0) ? ("Cuenta: " + twitch.Account + ". Se activa al encender el chat. Si conectaste Twitch antes de esta versión, vuelve a conectarlo una vez para dar el permiso de follows.") : ("Cuenta: " + twitch.Account + ". Estado: " + chatHub.FollowsState + ".")));
					Theme.DrawWrapped(g, s, Theme.Body(8.5f), Theme.Text, r);
				}
			};
			Place(card2, info, 20f, 60f, 380f, 60f);
			VButton conn = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = (twitch.Connected ? "Volver a conectar Twitch" : "Conectar Twitch"),
				FontSize = 9f
			};
			Place(card2, conn, 20f, 126f, 220f, 36f);
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
					if (chatHub != null && twitch.Connected)
					{
						StartChat(false);
					}
				}
			};
			Card card3 = new Card();
			card3.Title = "Donaciones";
			card3.Subtitle = "Pega el token del servicio que uses (o los dos).";
			Card card4 = card3;
			Place(f, card4, 20f, 206f, 420f, 260f);
			Label(card4, "STREAMELEMENTS · JWT TOKEN", 20f, 60f, 260f);
			VButton vButton = new VButton();
			vButton.Kind = ButtonKind.Link;
			vButton.Text = "¿Dónde está?";
			vButton.FontSize = 8.25f;
			VButton vButton2 = vButton;
			Place(card4, vButton2, 280f, 58f, 120f, 22f);
			vButton2.Click += delegate
			{
				Http.OpenBrowser("https://streamelements.com/dashboard/account/channels");
			};
			Field fSe = new Field();
			Place(card4, fSe, 20f, 80f, 380f, 36f);
			fSe.Box.UseSystemPasswordChar = true;
			fSe.Box.Text = seToken;
			Label(card4, "STREAMLABS · SOCKET API TOKEN", 20f, 126f, 260f);
			VButton vButton3 = new VButton();
			vButton3.Kind = ButtonKind.Link;
			vButton3.Text = "¿Dónde está?";
			vButton3.FontSize = 8.25f;
			VButton vButton4 = vButton3;
			Place(card4, vButton4, 280f, 124f, 120f, 22f);
			vButton4.Click += delegate
			{
				Http.OpenBrowser("https://streamlabs.com/dashboard#/settings/api-settings");
			};
			Field fSl = new Field();
			Place(card4, fSl, 20f, 146f, 380f, 36f);
			fSl.Box.UseSystemPasswordChar = true;
			fSl.Box.Text = slToken;
			Box(card4, 20f, 190f, 380f, 60f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "StreamElements: Cuenta → Canales → \"Mostrar secretos\" → JWT Token. Streamlabs: Ajustes → API Settings → API Tokens → \"Your Socket API Token\". Se guardan cifrados en StreamDelay.ini.", Theme.Body(7.75f), Theme.Muted, r);
			});
			VButton vButton5 = new VButton();
			vButton5.Kind = ButtonKind.Ghost;
			vButton5.Text = "Cancelar";
			vButton5.FontSize = 10f;
			VButton vButton6 = vButton5;
			VButton vButton7 = new VButton();
			vButton7.Kind = ButtonKind.Primary;
			vButton7.Text = "Guardar";
			vButton7.FontSize = 10f;
			VButton vButton8 = vButton7;
			Place(f, vButton6, 20f, 482f, 130f, 42f);
			Place(f, vButton8, 160f, 482f, 280f, 42f);
			vButton6.Click += delegate
			{
				f.Close();
			};
			vButton8.Click += delegate
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

		private static int S(float v)
		{
			return Theme.S(v);
		}

		public MainForm()
		{
			Text = "Stream Delay " + Version + " · Vortexs";
			Font = Theme.Body(9f);
			BackColor = Theme.Bg;
			ForeColor = Theme.Text;
			base.FormBorderStyle = FormBorderStyle.FixedSingle;
			base.MaximizeBox = false;
			base.AutoScaleMode = AutoScaleMode.None;
			base.ClientSize = new Size(S(460f), S(646f));
			base.StartPosition = FormStartPosition.CenterScreen;
			DoubleBuffered = true;
			try
			{
				base.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			}
			catch
			{
			}
			try
			{
				Stream manifestResourceStream = typeof(MainForm).Assembly.GetManifestResourceStream("StreamDelay.logo.png");
				if (manifestResourceStream != null)
				{
					logo = Image.FromStream(manifestResourceStream);
				}
			}
			catch
			{
			}
			int num = 460;
			int y = 118;
			header = Box(this, 0f, 0f, num, 70f, PaintHeader);
			tabs = new Tabs
			{
				Items = new string[7] { "Directo", "Chat", "Overlays", "Mando", "Título", "Ajustes", "Registro" }
			};
			Place(this, tabs, 20f, 72f, 420f, 36f);
			tabs.SelectedChanged += delegate
			{
				ShowPage(TabPages[tabs.Selected]);
			};
			BuildLive(y);
			BuildSettings(y);
			BuildLog(y);
			BuildTitle(y);
			BuildChat(y);
			engine.Log = delegate(string msg)
			{
				lock (pendingLog)
				{
					pendingLog.Enqueue(DateTime.Now.ToString("HH:mm:ss ") + msg);
				}
			};
			LoadConfig();
			BuildOverlays(y);
			BuildMando(y);
			RefreshPresetButtons();
			fldObs.Box.Text = ObsServer;
			ShowPage(0);
			timer = new System.Windows.Forms.Timer
			{
				Interval = 500
			};
			timer.Tick += delegate
			{
				pulse = !pulse;
				RefreshStatus();
				CheckHealth();
			};
			timer.Start();
			RefreshStatus();
			AppendLog("Stream Delay " + Version + ". Atajos: Ctrl+Alt+D activar/desactivar · Ctrl+Alt+↑/↓ ±5 s");
			AppendLog("Para varias calidades: en OBS usa el servicio Twitch con servidor personalizado " + ObsServer + " y activa Transmisión mejorada.");
		}

		private void BuildLive(int y0)
		{
			Card card = new Card();
			card.Accent = true;
			Card card2 = card;
			Place(this, card2, 20f, y0, 420f, 256f, 0);
			Box(card2, 20f, 12f, 240f, 22f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "DELAY EN DIRECTO", Theme.Mono(7.5f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			});
			tgDelay = new Toggle
			{
				Text = "Activado"
			};
			Place(card2, tgDelay, 268f, 9f, 136f, 28f);
			tgDelay.CheckedChanged += delegate
			{
				if (!loading && tgDelay.Checked != engine.Enabled)
				{
					SetEnabled(tgDelay.Checked);
				}
			};
			heroBox = Box(card2, 0f, 36f, 420f, 88f, PaintHero);
			slider = new Slider
			{
				Maximum = 600
			};
			Place(card2, slider, 14f, 122f, 392f, 34f);
			slider.ValueChanged += delegate
			{
				UpdateApply();
				heroBox.Invalidate();
			};
			slider.KeyDown += delegate(object o, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					ApplyDelay(slider.Value);
				}
			};
			int[] array = new int[6] { 10, 15, 30, 45, 60, 120 };
			presets = new VButton[array.Length];
			float num = 340f / (float)array.Length;
			for (int num2 = 0; num2 < array.Length; num2++)
			{
				int v = array[num2];
				VButton vButton = new VButton();
				vButton.Kind = ButtonKind.Pill;
				vButton.Text = v + "s";
				vButton.FontSize = 8.5f;
				VButton vButton2 = vButton;
				Place(card2, vButton2, 20f + (float)num2 * (num + 8f), 162f, num, 28f);
				int pi = num2;
				vButton2.Click += delegate
				{
					ApplyPreset(pi);
				};
				presets[num2] = vButton2;
			}
			btnApply = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Aplicar delay",
				FontSize = 10f
			};
			Place(card2, btnApply, 20f, 202f, 380f, 38f);
			btnApply.Click += delegate
			{
				ApplyDelay(slider.Value);
			};
			int num3 = y0 + 268;
			destStrip = Box(this, 20f, num3, 420f, 76f, PaintDestStrip, 0);
			destStrip.Cursor = Cursors.Hand;
			destStrip.Click += delegate
			{
				if (!engine.Running)
				{
					GoPage(2);
				}
			};
			float num4 = 133.33333f;
			for (int num5 = 0; num5 < 3; num5++)
			{
				int idx = num5;
				tiles[num5] = Box(this, 20f + (float)num5 * (num4 + 10f), num3 + 86, num4, 58f, delegate(Graphics g, Rectangle r)
				{
					PaintTile(g, r, idx);
				}, 0);
			}
			// Tercer recuadro: overlay de delay para OBS (copia la URL de la fuente Navegador).
			tiles[2].Cursor = Cursors.Hand;
			tiles[2].Click += delegate
			{
				GoPage(5);
			};
			int num6 = y0 + 422;
			btnStart = new VButton
			{
				Kind = ButtonKind.Primary,
				FontSize = 11f
			};
			Place(this, btnStart, 20f, num6, 296f, 46f, 0);
			btnStart.Click += delegate
			{
				ToggleService();
			};
			btnCut = new VButton
			{
				Kind = ButtonKind.Danger,
				Text = "Cortar ya",
				FontSize = 10f
			};
			Place(this, btnCut, 328f, num6, 112f, 46f, 0);
			btnCut.Click += delegate
			{
				if (MessageBox.Show(this, "Esto vacía el búfer y corta la transmisión en la plataforma ahora mismo. ¿Seguir?", "Cortar ya", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
				{
					engine.CutNow();
				}
			};
			footer = Box(this, 20f, num6 + 56, 420f, 38f, delegate(Graphics g, Rectangle r)
			{
				string prob = FirstProblem();
				if (prob != null)
				{
					// Algo falla ahora mismo: se queda en rojo hasta que se arregle.
					Theme.DrawText(g, "⚠ AVISO", Theme.Mono(6.75f), Theme.Red, new RectangleF(r.X, r.Y, r.Width, S(14f)), StringAlignment.Near, StringAlignment.Near);
					Theme.DrawText(g, prob, Theme.BodySemi(8.25f), Color.FromArgb(255, 120, 130), new RectangleF(r.X, r.Y + S(14f), r.Width, S(22f)), StringAlignment.Near, StringAlignment.Center);
					return;
				}
				Theme.DrawText(g, "ÚLTIMO EVENTO", Theme.Mono(6.75f), Theme.Dim, new RectangleF(r.X, r.Y, r.Width, S(14f)), StringAlignment.Near, StringAlignment.Near);
				Theme.DrawText(g, (lastLine.Length > 0) ? lastLine : "—", Theme.Body(8.25f), Theme.Muted, new RectangleF(r.X, r.Y + S(14f), r.Width, S(22f)), StringAlignment.Near, StringAlignment.Center);
			}, 0);
			footer.Cursor = Cursors.Hand;
			footer.Click += delegate
			{
				GoPage(3);
			};
		}

		private void BuildSettings(int y0)
		{
			BuildDestinations(y0);
			Card card = new Card();
			card.Title = "OBS";
			Card card2 = card;
			Place(this, card2, 20f, y0 + 218, 420f, 166f, 2);
			VButton vButton = new VButton();
			vButton.Kind = ButtonKind.Link;
			vButton.Text = "Cómo configurarlo →";
			vButton.FontSize = 8.5f;
			VButton vButton2 = vButton;
			Place(card2, vButton2, 220f, 13f, 180f, 24f);
			vButton2.Click += delegate
			{
				OpenGuide();
			};
			Label(card2, "SERVIDOR PERSONALIZADO PARA OBS", 20f, 42f, 300f);
			fldObs = new Field
			{
				ReadOnly = true
			};
			Place(card2, fldObs, 20f, 60f, 292f, 36f);
			btnCopyObs = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Copiar",
				FontSize = 9f
			};
			Place(card2, btnCopyObs, 320f, 60f, 80f, 36f);
			btnCopyObs.Click += delegate
			{
				CopyText(ObsServer, btnCopyObs, "Copiar");
			};
			Label(card2, "PUERTO", 20f, 102f, 80f);
			fldPort = new Field();
			Place(card2, fldPort, 20f, 120f, 84f, 34f);
			fldPort.Box.Text = "1935";
			fldPort.Box.Leave += delegate
			{
				CommitPort();
			};
			fldPort.Box.KeyDown += delegate(object o, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					CommitPort();
					e.SuppressKeyPress = true;
				}
			};
			tgLan = new Toggle
			{
				Text = "OBS en otro PC de la red",
				Hint = "Configuración de 2 PCs"
			};
			Place(card2, tgLan, 120f, 116f, 280f, 44f);
			tgLan.CheckedChanged += delegate
			{
				fldObs.Box.Text = ObsServer;
				if (!loading)
				{
					SaveConfig();
				}
			};
			Card card3 = new Card();
			Place(this, card3, 20f, y0 + 394, 420f, 124f, 2);
			tgReplay = new Toggle
			{
				Text = "Al subir el delay, repetir lo último (si no, se congela)"
			};
			Place(card3, tgReplay, 18f, 10f, 384f, 28f);
			tgReplay.Checked = true;
			tgReplay.CheckedChanged += delegate
			{
				engine.ReplayOnIncrease = tgReplay.Checked;
				if (!loading)
				{
					SaveConfig();
				}
			};
			tgTop = new Toggle
			{
				Text = "Ventana siempre visible"
			};
			Place(card3, tgTop, 18f, 40f, 384f, 28f);
			tgTop.CheckedChanged += delegate
			{
				base.TopMost = tgTop.Checked;
				if (!loading)
				{
					SaveConfig();
				}
			};
			tgFfmpeg = new Toggle
			{
				Text = "Modo clásico con ffmpeg (solo una calidad)"
			};
			Place(card3, tgFfmpeg, 18f, 70f, 384f, 28f);
			tgFfmpeg.CheckedChanged += delegate
			{
				if (!loading)
				{
					SaveConfig();
				}
			};
			Box(card3, 20f, 100f, 380f, 18f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "CTRL+ALT+D ACTIVAR · CTRL+ALT+↑/↓ ±5 S", Theme.Mono(7f), Theme.Dim, r, StringAlignment.Near, StringAlignment.Center);
			});
		}

		private void BuildLog(int y0)
		{
			Card card = new Card();
			card.Title = "Registro";
			card.Subtitle = "Si pides ayuda, copia este texto: no incluye tu clave.";
			Card card2 = card;
			Place(this, card2, 20f, y0, 420f, 440f, 3);
			PaintBox paintBox = Box(card2, 16f, 62f, 388f, 362f, delegate(Graphics g, Rectangle r)
			{
				using (GraphicsPath path = Theme.Round(new RectangleF((float)r.X + 0.5f, (float)r.Y + 0.5f, (float)r.Width - 1.5f, (float)r.Height - 1.5f), S(12f)))
				{
					using (SolidBrush brush = new SolidBrush(Theme.Input))
					{
						g.FillPath(brush, path);
					}
					using (Pen pen = new Pen(Theme.Border))
					{
						g.DrawPath(pen, path);
					}
				}
			});
			tbLog = new TextBox
			{
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				BorderStyle = BorderStyle.None,
				BackColor = Theme.Input,
				ForeColor = Color.FromArgb(212, 212, 218),
				Font = new Font("Consolas", 11.333333f * Theme.Scale, GraphicsUnit.Pixel),
				WordWrap = true
			};
			paintBox.Controls.Add(tbLog);
			tbLog.SetBounds(S(12f), S(10f), S(370f), S(342f));
			VButton copy = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Copiar registro",
				FontSize = 9.5f
			};
			Place(this, copy, 20f, y0 + 452, 204f, 40f, 3);
			copy.Click += delegate
			{
				CopyText((tbLog.Text.Length > 0) ? tbLog.Text : " ", copy, "Copiar registro");
			};
			VButton vButton = new VButton();
			vButton.Kind = ButtonKind.Ghost;
			vButton.Text = "Abrir guía";
			vButton.FontSize = 9.5f;
			VButton vButton2 = vButton;
			Place(this, vButton2, 236f, y0 + 452, 204f, 40f, 3);
			vButton2.Click += delegate
			{
				OpenGuide();
			};
		}

		private void Place(Control parent, Control c, float x, float y, float w, float h)
		{
			Place(parent, c, x, y, w, h, -1);
		}

		private void Place(Control parent, Control c, float x, float y, float w, float h, int page)
		{
			c.SetBounds(S(x), S(y), S(w), S(h));
			parent.Controls.Add(c);
			if (page >= 0)
			{
				pages[page].Add(c);
			}
		}

		private PaintBox Box(Control parent, float x, float y, float w, float h, Action<Graphics, Rectangle> painter)
		{
			return Box(parent, x, y, w, h, painter, -1);
		}

		private PaintBox Box(Control parent, float x, float y, float w, float h, Action<Graphics, Rectangle> painter, int page)
		{
			PaintBox paintBox = new PaintBox();
			paintBox.Painter = painter;
			PaintBox paintBox2 = paintBox;
			Place(parent, paintBox2, x, y, w, h, page);
			return paintBox2;
		}

		private void Label(Control parent, string text, float x, float y, float w)
		{
			Box(parent, x, y, w, 18f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, text, Theme.Mono(7f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			});
		}

		private void ShowPage(int p)
		{
			SuspendLayout();
			for (int i = 0; i < pages.Length; i++)
			{
				foreach (Control item in pages[i])
				{
					item.Visible = i == p;
				}
			}
			ResumeLayout();
			if (p == 3)
			{
				tbLog.SelectionStart = tbLog.TextLength;
				tbLog.ScrollToCaret();
			}
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			if (bg == null || bg.Size != base.ClientSize)
			{
				BuildBackground();
			}
			e.Graphics.DrawImageUnscaled(bg, 0, 0);
		}

		private void BuildBackground()
		{
			if (bg != null)
			{
				bg.Dispose();
			}
			bg = new Bitmap(Math.Max(1, base.ClientSize.Width), Math.Max(1, base.ClientSize.Height));
			using (Graphics graphics = Graphics.FromImage(bg))
			{
				Theme.Quality(graphics);
				graphics.Clear(Theme.Bg);
				float num = bg.Width;
				float num2 = bg.Height;
				Card.Glow(graphics, new RectangleF((0f - num) * 0.35f, (0f - num2) * 0.22f, num * 1.1f, num2 * 0.55f), Color.FromArgb(48, Theme.Violet));
				Card.Glow(graphics, new RectangleF(num * 0.45f, (0f - num2) * 0.05f, num * 0.9f, num2 * 0.5f), Color.FromArgb(30, Theme.Yellow));
				Card.Glow(graphics, new RectangleF((0f - num) * 0.2f, num2 * 0.7f, num * 1f, num2 * 0.45f), Color.FromArgb(22, Theme.Violet));
				float num3 = S(22f);
				float num4 = S(70f);
				for (float num5 = num3 / 2f; num5 < num4; num5 += num3)
				{
					for (float num6 = num3 / 2f; num6 < num; num6 += num3)
					{
						int val = (int)(40f * (1f - num5 / num4));
						using (SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Max(0, val), Theme.Violet)))
						{
							graphics.FillEllipse(brush, num6 - 1f, num5 - 1f, 2f, 2f);
						}
					}
				}
				using (Pen pen = new Pen(Color.FromArgb(18, 255, 255, 255)))
				{
					graphics.DrawLine(pen, 0f, num4 - 1f, num, num4 - 1f);
				}
			}
		}

		private void PaintHeader(Graphics g, Rectangle r)
		{
			float num = S(20f);
			if (logo != null)
			{
				g.DrawImage(logo, new RectangleF(num, S(16f), S(38f), S(38f)));
			}
			float num2 = num + (float)S(50f);
			Font font = Theme.Display(16f);
			float num3 = Theme.Measure(g, "Stream", font).Width + (float)S(7f);
			SizeF sizeF = Theme.Measure(g, "Delay", font);
			using (SolidBrush brush = new SolidBrush(Theme.Text))
			{
				g.DrawString("Stream", font, brush, num2, S(10f), StringFormat.GenericTypographic);
			}
			using (LinearGradientBrush brush2 = Theme.Brand(new RectangleF(num2 + num3, S(10f), sizeF.Width, sizeF.Height), true))
			{
				g.DrawString("Delay", font, brush2, num2 + num3, S(10f), StringFormat.GenericTypographic);
			}
			Theme.DrawText(g, "BY VORTEXS · V2.6", Theme.Mono(7f), Theme.Muted, new RectangleF(num2 + 1f, S(39f), S(200f), S(16f)), StringAlignment.Near, StringAlignment.Center);
			bool flag = true;
			string s;
			Color color;
			if (!engine.Running)
			{
				s = "APAGADO";
				color = Theme.Dim;
				flag = false;
			}
			else if (engine.OutputActive)
			{
				int liveCount = engine.LiveCount;
				s = (engine.Replaying ? "REPITIENDO" : ((liveCount > 1) ? ("EN DIRECTO · " + liveCount) : "EN DIRECTO"));
				color = (engine.Replaying ? Theme.Violet : Theme.Red);
			}
			else if (engine.InputActive)
			{
				s = "CARGANDO DELAY";
				color = Theme.Yellow;
			}
			else
			{
				s = "ESPERANDO A OBS";
				color = Theme.Violet;
			}
			Font f = Theme.Mono(7.5f);
			float width = Theme.Measure(g, s, f).Width;
			float num4 = width + (float)S(36f);
			float num5 = S(28f);
			RectangleF r2 = new RectangleF((float)(r.Width - S(20f)) - num4, S(21f), num4, num5);
			using (GraphicsPath path = Theme.Round(r2, num5 / 2f))
			{
				using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(28, color)))
				{
					g.FillPath(brush3, path);
				}
				using (Pen pen = new Pen(Color.FromArgb(110, color)))
				{
					g.DrawPath(pen, path);
				}
			}
			float num6 = S(8f);
			RectangleF rect = new RectangleF(r2.X + (float)S(12f), r2.Y + (num5 - num6) / 2f, num6, num6);
			if (flag)
			{
				if (pulse && engine.OutputActive)
				{
					Card.Glow(g, RectangleF.Inflate(rect, num6, num6), Color.FromArgb(120, color));
				}
				using (SolidBrush brush4 = new SolidBrush(color))
				{
					g.FillEllipse(brush4, rect);
				}
			}
			else
			{
				using (Pen pen2 = new Pen(color, 1.5f))
				{
					g.DrawEllipse(pen2, rect);
				}
			}
			Theme.DrawText(g, s, f, flag ? Theme.Mix(color, Color.White, 0.35f) : Theme.Muted, new RectangleF(rect.Right + (float)S(7f), r2.Y, width + (float)S(8f), num5), StringAlignment.Near, StringAlignment.Center);
		}

		private void PaintHero(Graphics g, Rectangle r)
		{
			int num = engine.TargetMs / 1000;
			bool enabled = engine.Enabled;
			float num2 = S(18f);
			Font font = Theme.Display(40f);
			string s = num.ToString();
			SizeF sizeF = Theme.Measure(g, s, font);
			RectangleF r2 = new RectangleF(num2, 0f, sizeF.Width, sizeF.Height);
			if (enabled)
			{
				using (LinearGradientBrush brush = Theme.Brand(r2, true))
				{
					g.DrawString(s, font, brush, num2, 0f, StringFormat.GenericTypographic);
				}
			}
			else
			{
				using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(82, 82, 92)))
				{
					g.DrawString(s, font, brush2, num2, 0f, StringFormat.GenericTypographic);
				}
			}
			Font font2 = Theme.Display(17f);
			float num3 = num2 + sizeF.Width + (float)S(6f);
			SizeF sizeF2 = Theme.Measure(g, "s", font2);
			float y = Theme.Ascent(g, font) - Theme.Ascent(g, font2);
			using (SolidBrush brush3 = new SolidBrush(enabled ? Theme.Muted : Theme.Dim))
			{
				g.DrawString("s", font2, brush3, num3, y, StringFormat.GenericTypographic);
			}
			if (slider.Value != num)
			{
				float x = num3 + sizeF2.Width + (float)S(18f);
				Font font3 = Theme.Display(15f);
				string s2 = "→ " + slider.Value + " s";
				using (SolidBrush brush4 = new SolidBrush(Theme.Yellow))
				{
					g.DrawString(s2, font3, brush4, x, S(10f), StringFormat.GenericTypographic);
				}
				Theme.DrawText(g, "SIN APLICAR", Theme.Mono(7f), Theme.Mix(Theme.Yellow, Theme.Bg, 0.35f), new RectangleF(x, S(36f), S(160f), S(16f)), StringAlignment.Near, StringAlignment.Center);
			}
			string s3;
			Color c;
			if (!enabled)
			{
				s3 = "DESACTIVADO · EL PÚBLICO VE EL DIRECTO SIN RETRASO";
				c = Theme.Red;
			}
			else if (engine.Replaying)
			{
				s3 = "REPITIENDO LO ÚLTIMO MIENTRAS SUBE EL DELAY";
				c = Theme.Violet;
			}
			else if (engine.OutputActive)
			{
				s3 = "DELAY REAL " + ((double)engine.CurrentDelayMs / 1000.0).ToString("0.0") + " S · BÚFER " + ((double)engine.QueuedBytes / 1048576.0).ToString("0") + " MB";
				c = Theme.Muted;
			}
			else
			{
				s3 = ((num >= 60) ? FormatMin(num) : "SEGUNDOS DE RETRASO");
				c = Theme.Dim;
			}
			Theme.DrawText(g, s3, Theme.Mono(7.5f), c, new RectangleF(num2 + 2f, S(64f), (float)r.Width - num2 - (float)S(20f), S(20f)), StringAlignment.Near, StringAlignment.Center);
		}

		private static string FormatMin(int s)
		{
			int num = s / 60;
			int num2 = s % 60;
			return num + " MIN" + ((num2 > 0) ? (" " + num2 + " S") : "") + " DE RETRASO";
		}

		private static string Cap(string s)
		{
			if (s.Length != 0)
			{
				return s.Substring(0, 1) + s.Substring(1).ToLowerInvariant();
			}
			return s;
		}

		private void PaintTile(Graphics g, Rectangle r, int i)
		{
			RectangleF r2 = new RectangleF(0.5f, 0.5f, (float)r.Width - 1.5f, (float)r.Height - 1.5f);
			using (GraphicsPath path = Theme.Round(r2, S(14f)))
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
			string text = null;
			bool running = engine.Running;
			string s;
			string s2;
			Color color;
			switch (i)
			{
			case 0:
				s = "OBS";
				if (!running)
				{
					s2 = "—";
					color = Theme.Dim;
				}
				else if (engine.InputActive)
				{
					s2 = "Conectado";
					color = Theme.Green;
				}
				else
				{
					s2 = "Esperando…";
					color = Theme.Yellow;
				}
				break;
			case 1:
			{
				s = "CALIDADES";
				string qualities = engine.Qualities;
				int topHeight = engine.TopHeight;
				bool enabled = dTwitch.Enabled;
				int inputTracks = engine.InputTracks;
				if (qualities.Length == 0)
				{
					s2 = ((inputTracks == 0) ? "—" : (inputTracks + ((inputTracks == 1) ? " calidad" : " calidades")));
					color = Theme.Dim;
					break;
				}
				string[] array = qualities.Split(new string[1] { " · " }, StringSplitOptions.None);
				s2 = array[0] + ((array.Length > 1) ? (" +" + (array.Length - 1)) : "");
				if (topHeight < 1080)
				{
					color = Theme.Red;
					text = "Sin 1080p: OBS → Vídeo";
				}
				else if (array.Length > 1)
				{
					color = Theme.Green;
					text = string.Join(" · ", array, 1, array.Length - 1);
				}
				else if (enabled)
				{
					color = Theme.Yellow;
					text = "Twitch: 1 sola calidad";
				}
				else
				{
					color = Theme.Green;
					text = "Cada plataforma crea el resto";
				}
				break;
			}
			default:
				s = "OVERLAYS OBS";
				s2 = "Gestionar";
				text = "Delay · espectadores · chat";
				color = chatServer.Running ? Theme.Violet : Theme.Dim;
				break;
			}
			float num = S(7f);
			using (SolidBrush brush2 = new SolidBrush(color))
			{
				g.FillEllipse(brush2, S(16f), S(14f), num, num);
			}
			Theme.DrawText(g, s, Theme.Mono(7f), Theme.Muted, new RectangleF(S(30f), S(8f), r.Width - S(40f), S(18f)), StringAlignment.Near, StringAlignment.Center);
			float y = ((text != null) ? S(23f) : S(27f));
			Theme.DrawText(g, s2, Theme.BodyBold(10.5f), Theme.Text, new RectangleF(S(15f), y, r.Width - S(24f), S(24f)), StringAlignment.Near, StringAlignment.Center);
			if (text != null)
			{
				Theme.DrawText(g, text, Theme.Body(7.25f), (color == Theme.Red) ? Color.FromArgb(255, 120, 130) : ((color == Theme.Yellow) ? Theme.Yellow : Theme.Muted), new RectangleF(S(15f), S(43f), r.Width - S(24f), S(14f)), StringAlignment.Near, StringAlignment.Center);
			}
		}

		private void ToggleService()
		{
			if (engine.Running)
			{
				if (!engine.OutputActive || MessageBox.Show(this, "Se está transmitiendo. Detener corta la transmisión. ¿Seguir?", "Detener", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
				{
					engine.Stop();
					AppendLog("Servicio detenido.");
					RefreshStatus();
				}
				return;
			}
			CommitPort();
			if (!ValidateDestinations())
			{
				return;
			}
			engine.UseFfmpeg = tgFfmpeg.Checked;
			if (engine.UseFfmpeg)
			{
				bool needTls = false;
				foreach (Destination item in EnabledDestinations())
				{
					if (item.Server.Trim().StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
					{
						needTls = true;
					}
				}
				List<string> list = Engine.FfmpegCandidates();
				if (list.Count == 0)
				{
					OfferFfmpegInstall("El modo clásico necesita ffmpeg (gratuito) y no lo encuentra en este PC.");
					return;
				}
				string text = null;
				string text2 = null;
				foreach (string item2 in list)
				{
					string text3 = Engine.CheckFfmpeg(item2, needTls);
					if (text3 == null)
					{
						text = item2;
						break;
					}
					if (text2 == null)
					{
						text2 = item2 + "\n→ " + text3;
					}
				}
				if (text == null)
				{
					OfferFfmpegInstall("El ffmpeg que hay en este PC no sirve:\n" + text2);
					return;
				}
				engine.FfmpegPath = text;
				AppendLog("Modo clásico: ffmpeg " + text + " (solo se envía la calidad principal).");
			}
			engine.InputPort = port;
			engine.AllowLan = tgLan.Checked;
			engine.Destinations = EnabledDestinations();
			try
			{
				engine.Start();
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message + "\n\nPuede que otro programa use ese puerto (otro servidor RTMP u otra copia de StreamDelay).\nCambia el puerto en Ajustes → OBS (por ejemplo a 1936) y copia de nuevo el servidor en OBS.", "No se puede iniciar", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			SaveConfig();
			AppendLog("Esperando a OBS en " + ObsServer + ". Destinos: " + DestSummary() + ".");
			if (dTwitch.Enabled && dTwitch.Key.Trim().Length == 0)
			{
				AppendLog("Twitch sin clave guardada: se usará la que ponga OBS (servicio Twitch).");
			}
			if (tgLan.Checked)
			{
				AppendLog("Si Windows pregunta por el Firewall, permite el acceso en redes privadas.");
			}
			RefreshStatus();
		}

		private void OfferFfmpegInstall(string reason)
		{
			DialogResult dialogResult = MessageBox.Show(this, reason + "\n\n¿Quieres instalar ffmpeg ahora con winget? Se abrirá una ventana; acepta lo que pida y, cuando termine, vuelve a pulsar \"Iniciar servicio\".\n\n(También puedes desactivar el modo clásico: StreamDelay ya no necesita ffmpeg.)", "Falta ffmpeg", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk);
			if (dialogResult != DialogResult.Yes)
			{
				return;
			}
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe", "/c winget install -e --id Gyan.FFmpeg & echo. & echo Listo. Ya puedes cerrar esta ventana. & pause");
				processStartInfo.UseShellExecute = true;
				Process.Start(processStartInfo);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "No se pudo abrir winget (" + ex.Message + ").\n\nDescarga ffmpeg de gyan.dev/ffmpeg/builds y copia ffmpeg.exe junto a StreamDelay.exe.", "Falta ffmpeg");
			}
		}

		private void OpenGuide()
		{
			string fileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GUIA.html");
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo(fileName);
				processStartInfo.UseShellExecute = true;
				Process.Start(processStartInfo);
			}
			catch
			{
				MessageBox.Show(this, "No encuentro GUIA.html junto a StreamDelay.exe.", "Guía");
			}
		}

		private void CopyText(string text, VButton b, string original)
		{
			try
			{
				Clipboard.SetText(text);
			}
			catch
			{
				return;
			}
			b.Text = "¡Copiado!";
			System.Windows.Forms.Timer t = new System.Windows.Forms.Timer
			{
				Interval = 1300
			};
			t.Tick += delegate
			{
				b.Text = original;
				t.Dispose();
			};
			t.Start();
		}

		private void CommitPort()
		{
			int result;
			if (int.TryParse(fldPort.Box.Text.Trim(), out result) && result >= 1024 && result <= 65535)
			{
				port = result;
			}
			fldPort.Box.Text = port.ToString();
			fldObs.Box.Text = ObsServer;
			if (!loading)
			{
				SaveConfig();
			}
		}

		private void ApplyDelay(int seconds)
		{
			seconds = Math.Max(0, Math.Min(600, seconds));
			int num = engine.TargetMs / 1000;
			engine.TargetMs = seconds * 1000;
			if (slider.Value != seconds)
			{
				slider.Value = seconds;
			}
			if (engine.OutputActive && engine.Enabled && seconds != num)
			{
				AppendLog((seconds > num) ? ("Delay ↑ a " + seconds + " s: el público verá " + (engine.ReplayOnIncrease ? "una repetición" : "una pausa") + " de " + (seconds - num) + " s.") : ("Delay ↓ a " + seconds + " s: el público saltará " + (num - seconds) + " s."));
			}
			else if (seconds != num)
			{
				AppendLog("Delay: " + seconds + " s.");
			}
			if (engine.Enabled && seconds != num && seconds > 0)
			{
				AnnounceDelaySoon();
			}
			SaveConfig();
			RefreshStatus();
		}

		private void SetEnabled(bool on)
		{
			engine.Enabled = on;
			AppendLog(on ? "Delay activado." : "Delay desactivado (en directo).");
			AnnounceDelaySoon();
			SaveConfig();
			RefreshStatus();
		}

		private void UpdateApply()
		{
			bool flag = slider.Value != engine.TargetMs / 1000;
			btnApply.Muted = !flag;
			btnApply.Text = (flag ? ("Aplicar " + slider.Value + " s") : "Delay aplicado");
			btnApply.Invalidate();
			RefreshPresetButtons();
		}

		private void RefreshStatus()
		{
			lock (pendingLog)
			{
				while (pendingLog.Count > 0)
				{
					AppendLog(pendingLog.Dequeue());
				}
			}
			if (engine.LowQualityAlert)
			{
				engine.LowQualityAlert = false;
				tabs.Selected = 0;
				timer.Stop();
				MessageBox.Show(this, "OBS está enviando como máximo " + engine.TopHeight + "p: tu público no tendrá 1080p.\n\nAún no ha salido nada en directo (está en el delay). Para arreglarlo:\n1. Detén la transmisión en OBS.\n2. OBS → Ajustes → Vídeo → Resolución de salida (escalada): 1920x1080.\n3. Si usas Transmisión mejorada: Ajustes → Emisión → Ancho de banda máximo: Auto.\n4. Vuelve a iniciar la transmisión.", "Sin calidad 1080p", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				timer.Start();
			}
			if (tgDelay.Checked != engine.Enabled)
			{
				loading = true;
				tgDelay.Checked = engine.Enabled;
				loading = false;
			}
			tgDelay.Text = (engine.Enabled ? "Activado" : "Desactivado");
			UpdateApply();
			bool running = engine.Running;
			int count = EnabledDestinations().Count;
			btnStart.Text = (running ? "■  Detener servicio" : ((count == 0) ? "▶  Iniciar servicio" : ((count <= 2) ? ("▶  Iniciar · " + DestSummary()) : ("▶  Iniciar en " + count + " destinos"))));
			btnStart.Kind = (running ? ButtonKind.Ghost : ButtonKind.Primary);
			btnStart.Invalidate();
			btnCut.Enabled = running;
			fldPort.Locked = running;
			Toggle toggle = tgLan;
			bool enabled = (tgFfmpeg.Enabled = !running);
			toggle.Enabled = enabled;
			RefreshDestinationsUi();
			header.Invalidate();
			heroBox.Invalidate();
			PaintBox[] array = tiles;
			foreach (PaintBox paintBox in array)
			{
				paintBox.Invalidate();
			}
		}

		private void AppendLog(string s)
		{
			if (tbLog.TextLength > 40000)
			{
				tbLog.Text = tbLog.Text.Substring(20000);
			}
			tbLog.AppendText(s + Environment.NewLine);
			lastLine = s;
			if (footer != null)
			{
				footer.Invalidate();
			}
		}

		[DllImport("user32.dll")]
		private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

		[DllImport("user32.dll")]
		private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Theme.DarkTitleBar(base.Handle);
			uint mods = 16387u;
			lock (pendingLog)
			{
				if (!RegisterHotKey(base.Handle, 1, mods, 68u))
				{
					pendingLog.Enqueue("Aviso: Ctrl+Alt+D ya lo usa otro programa.");
				}
				if (!RegisterHotKey(base.Handle, 2, mods, 38u))
				{
					pendingLog.Enqueue("Aviso: Ctrl+Alt+↑ ya lo usa otro programa.");
				}
				if (!RegisterHotKey(base.Handle, 3, mods, 40u))
				{
					pendingLog.Enqueue("Aviso: Ctrl+Alt+↓ ya lo usa otro programa.");
				}
			}
		}

		protected override void WndProc(ref Message msg)
		{
			if (msg.Msg == 786)
			{
				switch (msg.WParam.ToInt32())
				{
				case 1:
					SetEnabled(!engine.Enabled);
					break;
				case 2:
					ApplyDelay(engine.TargetMs / 1000 + 5);
					break;
				case 3:
					ApplyDelay(engine.TargetMs / 1000 - 5);
					break;
				}
			}
			base.WndProc(ref msg);
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (engine.OutputActive && MessageBox.Show(this, "Se está transmitiendo. Cerrar corta la transmisión. ¿Seguir?", "Cerrar", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				e.Cancel = true;
				return;
			}
			for (int i = 1; i <= 3; i++)
			{
				UnregisterHotKey(base.Handle, i);
			}
			SaveConfig();
			engine.Stop();
			base.OnFormClosing(e);
		}

		private void LoadConfig()
		{
			loading = true;
			slider.Value = 30;
			engine.TargetMs = 30000;
			tgDelay.Checked = true;
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			if (File.Exists(configPath))
			{
				string[] array = File.ReadAllLines(configPath);
				foreach (string text in array)
				{
					int num = text.IndexOf('=');
					if (num < 0)
					{
						continue;
					}
					string text2 = text.Substring(0, num);
					string text3 = (dictionary[text2] = text.Substring(num + 1));
					try
					{
						switch (text2)
						{
						case "delay":
						{
							int num2 = Math.Max(0, Math.Min(600, int.Parse(text3)));
							slider.Value = num2;
							engine.TargetMs = num2 * 1000;
							break;
						}
						case "enabled":
							engine.Enabled = text3 == "1";
							tgDelay.Checked = engine.Enabled;
							break;
						case "topmost":
							tgTop.Checked = text3 == "1";
							break;
						case "replay":
							tgReplay.Checked = text3 == "1";
							engine.ReplayOnIncrease = tgReplay.Checked;
							break;
						case "port":
							port = Math.Max(1024, Math.Min(65535, int.Parse(text3)));
							fldPort.Box.Text = port.ToString();
							break;
						case "lan":
							tgLan.Checked = text3 == "1";
							break;
						case "ffmpeg":
							tgFfmpeg.Checked = text3 == "1";
							break;
						}
					}
					catch
					{
					}
				}
			}
			base.TopMost = tgTop.Checked;
			LoadDestinations(dictionary);
			LoadTitleConfig(dictionary);
			LoadChatConfig(dictionary);
			loading = false;
		}

		private void SaveConfig()
		{
			try
			{
				List<string> list = new List<string>();
				list.Add("delay=" + engine.TargetMs / 1000);
				list.Add("enabled=" + (engine.Enabled ? "1" : "0"));
				list.Add("topmost=" + (tgTop.Checked ? "1" : "0"));
				list.Add("replay=" + (tgReplay.Checked ? "1" : "0"));
				list.Add("port=" + port);
				list.Add("lan=" + (tgLan.Checked ? "1" : "0"));
				list.Add("ffmpeg=" + (tgFfmpeg.Checked ? "1" : "0"));
				List<string> list2 = list;
				SaveDestinations(list2);
				SaveTitleConfig(list2);
				SaveChatConfig(list2);
				File.WriteAllLines(configPath, list2.ToArray());
			}
			catch
			{
			}
		}
	}
}
