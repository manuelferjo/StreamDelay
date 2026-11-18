// Ventana "Personalizar chat" (pestaña Overlays → Chat en pantalla → Personalizar chat →).
// Cada cambio se ve al momento en OBS (la página del chat lee el estilo cada 2 s);
// "Cancelar" o cerrar la ventana lo deja como estaba.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private ChatStyle chatStyle = new ChatStyle();

		private void ChatStyleDialog()
		{
			ChatStyle before = chatStyle.Clone();
			bool saved = false;
			Form f = DarkForm("Personalizar chat", 760, 580);
			List<Action> refresh = new List<Action>();
			Action changed = delegate
			{
				foreach (Action a in refresh)
				{
					a();
				}
			};

			// Grupo de píldoras: isOn(i) dice si la i está elegida y pick(i) la elige.
			Func<Control, string[], float, float, float, int, Func<int, bool>, Action<int>, VButton[]> pills = delegate(Control parent, string[] labels, float x, float y, float w, int perRow, Func<int, bool> isOn, Action<int> pick)
			{
				VButton[] b = new VButton[labels.Length];
				for (int i = 0; i < labels.Length; i++)
				{
					int idx = i;
					b[i] = new VButton { Kind = ButtonKind.Pill, Text = labels[i], FontSize = 7f };
					Place(parent, b[i], x + (float)(i % perRow) * (w + 6f), y + (float)(i / perRow) * 30f, w, 25f);
					b[i].Click += delegate
					{
						pick(idx);
						changed();
					};
				}
				refresh.Add(delegate
				{
					for (int i = 0; i < b.Length; i++)
					{
						b[i].Selected = isOn(i);
						b[i].Invalidate();
					}
				});
				return b;
			};

			// Muestra de color que abre el selector de color de Windows.
			Action<Control, string, float, float, Func<string>, Action<string>> swatch = delegate(Control parent, string name, float x, float y, Func<string> get, Action<string> set)
			{
				PaintBox box = Box(parent, x, y, 74f, 46f, delegate(Graphics g, Rectangle r)
				{
					RectangleF chip = new RectangleF(0.5f, 0.5f, (float)r.Width - 1.5f, (float)S(26f));
					using (GraphicsPath path = Theme.Round(chip, S(8f)))
					{
						using (SolidBrush brush = new SolidBrush(OverlayStyle.ToColor(get())))
						{
							g.FillPath(brush, path);
						}
						using (Pen pen = new Pen(Theme.BorderHi))
						{
							g.DrawPath(pen, path);
						}
					}
					Theme.DrawText(g, name, Theme.Body(7.5f), Theme.Muted, new RectangleF(0f, S(28f), r.Width, S(18f)), StringAlignment.Center, StringAlignment.Center);
				});
				box.Cursor = Cursors.Hand;
				box.Click += delegate
				{
					using (ColorDialog cd = new ColorDialog { Color = OverlayStyle.ToColor(get()), FullOpen = true, AnyColor = true })
					{
						if (cd.ShowDialog(f) == DialogResult.OK)
						{
							set(OverlayStyle.ToHex(cd.Color));
							changed();
						}
					}
				};
				refresh.Add(delegate
				{
					box.Invalidate();
				});
			};

			Func<string, Action<bool>, Func<bool>, Toggle> toggle = delegate(string text, Action<bool> set, Func<bool> get)
			{
				Toggle t = new Toggle { Text = text };
				t.CheckedChanged += delegate
				{
					set(t.Checked);
				};
				refresh.Add(delegate
				{
					t.Checked = get();
				});
				return t;
			};

			// ---------- Columna izquierda: texto ----------
			Card left = new Card
			{
				Title = "Texto",
				Subtitle = "Fuente, tamaño y colores."
			};
			Place(f, left, 20f, 18f, 352f, 486f);
			Label(left, "ESTILOS LISTOS", 18f, 52f, 300f);
			pills(left, ChatStyle.PresetNames, 18f, 68f, 74f, 4, (int i) => false, delegate(int i)
			{
				chatStyle.ApplyPreset(ChatStyle.Preset(i));
			});
			Label(left, "FUENTE", 18f, 130f, 300f);
			string[] fontLabels = new string[16] { "Sistema", "Jakarta", "Inter", "Outfit", "Montserrat", "Poppins", "Rubik", "Nunito", "Oswald", "Bebas Neue", "Orbitron", "Bangers", "Comic Neue", "Perm. Marker", "Press Start", "Roboto Mono" };
			pills(left, fontLabels, 18f, 146f, 74f, 4, (int i) => chatStyle.Font == ChatStyle.Fonts[i], delegate(int i)
			{
				chatStyle.Font = ChatStyle.Fonts[i];
			});
			Label(left, "TAMAÑO DEL TEXTO", 18f, 272f, 300f);
			string[] sizeLabels = new string[8];
			for (int i = 0; i < 8; i++)
			{
				sizeLabels[i] = ChatStyle.Sizes[i].ToString();
			}
			pills(left, sizeLabels, 18f, 288f, 34.5f, 8, (int i) => chatStyle.Size == ChatStyle.Sizes[i], delegate(int i)
			{
				chatStyle.Size = ChatStyle.Sizes[i];
			});
			Label(left, "COLORES (PULSA PARA CAMBIARLOS)", 18f, 322f, 316f);
			swatch(left, "Texto", 18f, 338f, () => chatStyle.Text, delegate(string c) { chatStyle.Text = c; });
			swatch(left, "Nombres", 98f, 338f, () => chatStyle.NameColor, delegate(string c) { chatStyle.NameColor = c; chatStyle.NameMode = "fixed"; });
			swatch(left, "Raya", 178f, 338f, () => chatStyle.Accent, delegate(string c) { chatStyle.Accent = c; });
			swatch(left, "Fondo", 258f, 338f, () => chatStyle.BgColor, delegate(string c) { chatStyle.BgColor = c; if (chatStyle.Bg == "none") { chatStyle.Bg = "bubbles"; } });
			Toggle tNames = toggle("Cada nombre con su color", delegate(bool v) { chatStyle.NameMode = v ? "user" : "fixed"; }, () => chatStyle.NameMode == "user");
			Place(left, tNames, 18f, 388f, 316f, 28f);
			Label(left, "PARA QUE SE LEA SOBRE EL JUEGO", 18f, 422f, 316f);
			string[] fx = new string[3] { "shadow", "outline", "none" };
			pills(left, new string[3] { "Sombra", "Contorno", "Nada" }, 18f, 438f, 100f, 3, (int i) => chatStyle.Effect == fx[i], delegate(int i)
			{
				chatStyle.Effect = fx[i];
			});

			// ---------- Columna derecha: fondo y mensajes ----------
			Card right = new Card
			{
				Title = "Fondo y mensajes",
				Subtitle = "Qué se ve y cuánto dura."
			};
			Place(f, right, 384f, 18f, 356f, 486f);
			Label(right, "FONDO", 18f, 52f, 300f);
			string[] bgs = new string[3] { "none", "bubbles", "panel" };
			pills(right, new string[3] { "Sin fondo", "En cada mensaje", "Todo el chat" }, 18f, 68f, 102f, 3, (int i) => chatStyle.Bg == bgs[i], delegate(int i)
			{
				chatStyle.Bg = bgs[i];
			});
			Label(right, "OPACIDAD DEL FONDO", 18f, 102f, 300f);
			int[] ops = new int[5] { 100, 85, 70, 50, 30 };
			pills(right, new string[5] { "100 %", "85 %", "70 %", "50 %", "30 %" }, 18f, 118f, 58f, 5, (int i) => chatStyle.BgOpacity == ops[i], delegate(int i)
			{
				chatStyle.BgOpacity = ops[i];
			});
			Label(right, "ESQUINAS", 18f, 152f, 300f);
			string[] corners = new string[3] { "round", "pill", "rect" };
			pills(right, new string[3] { "Redondeadas", "Píldora", "Rectas" }, 18f, 168f, 102f, 3, (int i) => chatStyle.Corners == corners[i], delegate(int i)
			{
				chatStyle.Corners = corners[i];
			});
			Label(right, "QUÉ SE VE", 18f, 202f, 300f);
			Toggle tIcons = toggle("Icono de la plataforma", delegate(bool v) { chatStyle.Icons = v; }, () => chatStyle.Icons);
			Toggle tBadges = toggle("Insignias (mod, sub…)", delegate(bool v) { chatStyle.Badges = v; }, () => chatStyle.Badges);
			Toggle tEvents = toggle("Avisos (subs, regalos…)", delegate(bool v) { chatStyle.Events = v; }, () => chatStyle.Events);
			Toggle tCmd = toggle("Ocultar comandos (!…)", delegate(bool v) { chatStyle.HideCommands = v; }, () => chatStyle.HideCommands);
			Place(right, tIcons, 16f, 218f, 166f, 28f);
			Place(right, tBadges, 182f, 218f, 166f, 28f);
			Place(right, tEvents, 16f, 246f, 166f, 28f);
			Place(right, tCmd, 182f, 246f, 166f, 28f);
			Label(right, "CADA MENSAJE SE VE EN PANTALLA", 18f, 282f, 316f);
			int[] fades = new int[5] { 0, 15, 30, 60, 120 };
			pills(right, new string[5] { "Siempre", "15 s", "30 s", "1 min", "2 min" }, 18f, 298f, 58f, 5, (int i) => chatStyle.Fade == fades[i], delegate(int i)
			{
				chatStyle.Fade = fades[i];
			});
			Label(right, "AL LLEGAR", 18f, 332f, 300f);
			string[] anims = new string[4] { "slide", "fade", "pop", "none" };
			pills(right, new string[4] { "Deslizar", "Aparecer", "Saltar", "Nada" }, 18f, 348f, 74f, 4, (int i) => chatStyle.Anim == anims[i], delegate(int i)
			{
				chatStyle.Anim = anims[i];
			});
			Label(right, "ALINEACIÓN", 18f, 382f, 300f);
			string[] aligns = new string[2] { "left", "right" };
			pills(right, new string[2] { "Izquierda", "Derecha" }, 18f, 398f, 102f, 2, (int i) => chatStyle.Align == aligns[i], delegate(int i)
			{
				chatStyle.Align = aligns[i];
			});
			Toggle tPanel = toggle("Usar este estilo también en el panel del chat", delegate(bool v) { chatStyle.Panel = v; }, () => chatStyle.Panel);
			Place(right, tPanel, 16f, 436f, 330f, 28f);

			// ---------- Botones ----------
			VButton reset = new VButton { Kind = ButtonKind.Ghost, Text = "Restablecer", FontSize = 9.5f };
			VButton preview = new VButton { Kind = ButtonKind.Ghost, Text = "Vista previa", FontSize = 9.5f };
			VButton cancel = new VButton { Kind = ButtonKind.Ghost, Text = "Cancelar", FontSize = 10f };
			VButton save = new VButton { Kind = ButtonKind.Primary, Text = "Guardar", FontSize = 10f };
			Place(f, reset, 20f, 520f, 124f, 42f);
			Place(f, preview, 152f, 520f, 124f, 42f);
			Place(f, cancel, 488f, 520f, 112f, 42f);
			Place(f, save, 608f, 520f, 132f, 42f);
			reset.Click += delegate
			{
				chatStyle = new ChatStyle();
				changed();
			};
			preview.Click += delegate
			{
				// Mensajes de ejemplo sobre un fondo de cuadros (para ver lo transparente).
				string u = OverlayUrl("?overlay=1&demo=1");
				if (u != null)
				{
					Http.OpenBrowser(u);
				}
			};
			cancel.Click += delegate
			{
				f.Close();
			};
			save.Click += delegate
			{
				saved = true;
				SaveConfig();
				AppendLog("Estilo del chat guardado.");
				f.Close();
			};
			f.FormClosing += delegate
			{
				if (!saved)
				{
					chatStyle = before;
				}
			};
			changed();
			f.ShowDialog(this);
		}

		private void LoadChatStyleConfig(Dictionary<string, string> raw)
		{
			string v;
			if (raw.TryGetValue("chat_style", out v) && v.Length > 0)
			{
				chatStyle = ChatStyle.Load(v);
				return;
			}
			// Desde la 3.3: si tenías "Fondo en los mensajes del chat", se pasa al estilo del chat.
			chatStyle = new ChatStyle();
			if (ovStyle.ChatBubbles)
			{
				chatStyle.Bg = "bubbles";
				chatStyle.BgColor = ovStyle.Background;
				chatStyle.BgOpacity = Math.Max(30, ovStyle.Opacity);
				chatStyle.Accent = ovStyle.Accent;
				chatStyle.Text = ovStyle.Text;
				chatStyle.Effect = "none";
				if (Array.IndexOf(ChatStyle.Fonts, ovStyle.Font) >= 0)
				{
					chatStyle.Font = ovStyle.Font;
				}
			}
		}

		private void SaveChatStyleConfig(List<string> lines)
		{
			lines.Add("chat_style=" + chatStyle.Save());
		}
	}
}
