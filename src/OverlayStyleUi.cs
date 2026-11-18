// Ventana "Personalizar overlays" (pestaña Overlays → Personalizar →): estilos listos, colores,
// fondo, borde, brillo, forma, fuente y algunos detalles. Cada cambio se ve al momento en OBS
// (las páginas leen el estilo cada 2 s); "Cancelar" o cerrar la ventana lo deja como estaba.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private void OverlayStyleDialog()
		{
			OverlayStyle before = ovStyle.Clone();
			bool saved = false;
			Form f = DarkForm("Personalizar overlays", 760, 492);
			// Todo lo que hay que repintar al cambiar algo (sobre todo al elegir un estilo listo).
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
					Place(parent, b[i], x + (float)(i % perRow) * (w + 6f), y + (float)(i / perRow) * 32f, w, 26f);
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
			Func<Control, string, float, float, Func<string>, Action<string>, PaintBox> swatch = delegate(Control parent, string name, float x, float y, Func<string> get, Action<string> set)
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
				return box;
			};

			// ---------- Columna izquierda: colores y fondo ----------
			Card left = new Card
			{
				Title = "Colores y fondo",
				Subtitle = "Para el delay, los espectadores y el chat en pantalla."
			};
			Place(f, left, 20f, 18f, 352f, 400f);
			Label(left, "ESTILOS LISTOS", 18f, 54f, 300f);
			pills(left, OverlayStyle.PresetNames, 18f, 72f, 74f, 4, (int i) => false, delegate(int i)
			{
				ovStyle.ApplyPreset(OverlayStyle.Preset(i));
			});
			Label(left, "COLORES (PULSA PARA CAMBIARLOS)", 18f, 138f, 316f);
			swatch(left, "Principal", 18f, 156f, () => ovStyle.Accent, delegate(string c) { ovStyle.Accent = c; });
			swatch(left, "Segundo", 98f, 156f, () => ovStyle.Accent2, delegate(string c) { ovStyle.Accent2 = c; });
			swatch(left, "Fondo", 178f, 156f, () => ovStyle.Background, delegate(string c) { ovStyle.Background = c; });
			swatch(left, "Texto", 258f, 156f, () => ovStyle.Text, delegate(string c) { ovStyle.Text = c; });
			Toggle tGrad = new Toggle { Text = "Degradado del principal al segundo" };
			Place(left, tGrad, 18f, 206f, 316f, 28f);
			tGrad.CheckedChanged += delegate
			{
				ovStyle.Gradient = tGrad.Checked;
			};
			refresh.Add(delegate
			{
				tGrad.Checked = ovStyle.Gradient;
			});
			Label(left, "FONDO", 18f, 242f, 300f);
			int[] opacities = new int[5] { 100, 92, 70, 40, 0 };
			pills(left, new string[5] { "100 %", "92 %", "70 %", "40 %", "Sin fondo" }, 18f, 260f, 58f, 5, (int i) => ovStyle.Opacity == opacities[i], delegate(int i)
			{
				ovStyle.Opacity = opacities[i];
			});
			Label(left, "BORDE", 18f, 296f, 300f);
			int[] borders = new int[3] { 0, 2, 4 };
			pills(left, new string[3] { "Sin borde", "Fino", "Grueso" }, 18f, 314f, 66f, 3, (int i) => ovStyle.Border == borders[i], delegate(int i)
			{
				ovStyle.Border = borders[i];
			});
			Toggle tGlow = new Toggle { Text = "Brillo" };
			Place(left, tGlow, 240f, 312f, 98f, 28f);
			tGlow.CheckedChanged += delegate
			{
				ovStyle.Glow = tGlow.Checked;
			};
			refresh.Add(delegate
			{
				tGlow.Checked = ovStyle.Glow;
			});
			Box(left, 18f, 352f, 316f, 40f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Los cambios se ven al momento en OBS y en la vista previa.", Theme.Body(7.75f), Theme.Muted, r);
			});

			// ---------- Columna derecha: forma, fuente y detalles ----------
			Card right = new Card
			{
				Title = "Forma y texto"
			};
			Place(f, right, 384f, 18f, 356f, 400f);
			Label(right, "FORMA", 18f, 42f, 300f);
			string[] shapes = new string[4] { "round", "pill", "rect", "circle" };
			pills(right, new string[4] { "Redondeada", "Píldora", "Recta", "Círculo" }, 18f, 60f, 74f, 4, (int i) => ovStyle.Shape == shapes[i], delegate(int i)
			{
				ovStyle.Shape = shapes[i];
			});
			Label(right, "FUENTE", 18f, 96f, 300f);
			string[] fontLabels = new string[12] { "Outfit", "Montserrat", "Poppins", "Inter", "Bebas Neue", "Oswald", "Orbitron", "Russo One", "Rubik", "Bangers", "Perm. Marker", "Press Start" };
			pills(right, fontLabels, 18f, 114f, 74f, 4, (int i) => ovStyle.Font == OverlayStyle.Fonts[i], delegate(int i)
			{
				ovStyle.Font = OverlayStyle.Fonts[i];
			});
			Label(right, "TEXTO DEL OVERLAY DE DELAY", 18f, 216f, 316f);
			Field label = new Field();
			Place(right, label, 18f, 234f, 316f, 34f);
			label.Box.MaxLength = 24;
			label.Box.Text = ovStyle.Label;
			label.Box.TextChanged += delegate
			{
				ovStyle.Label = label.Box.Text.Trim();
			};
			Toggle tIcon = new Toggle { Text = "Punto que late (overlay de delay)" };
			Place(right, tIcon, 18f, 280f, 316f, 28f);
			tIcon.CheckedChanged += delegate
			{
				ovStyle.Icon = tIcon.Checked;
			};
			refresh.Add(delegate
			{
				tIcon.Checked = ovStyle.Icon;
			});
			Box(right, 18f, 312f, 316f, 30f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "El chat en pantalla tiene su propio estilo: Overlays → Chat en pantalla → Personalizar chat.", Theme.Body(7.75f), Theme.Violet, r);
			});
			Box(right, 18f, 348f, 316f, 40f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Vacío = «Delay». La forma Círculo es para el overlay de delay; los demás usan Píldora.", Theme.Body(7.75f), Theme.Muted, r);
			});

			// ---------- Botones ----------
			VButton reset = new VButton { Kind = ButtonKind.Ghost, Text = "Restablecer", FontSize = 9.5f };
			VButton preview = new VButton { Kind = ButtonKind.Ghost, Text = "Vista previa", FontSize = 9.5f };
			VButton cancel = new VButton { Kind = ButtonKind.Ghost, Text = "Cancelar", FontSize = 10f };
			VButton save = new VButton { Kind = ButtonKind.Primary, Text = "Guardar", FontSize = 10f };
			Place(f, reset, 20f, 434f, 124f, 42f);
			Place(f, preview, 152f, 434f, 124f, 42f);
			Place(f, cancel, 488f, 434f, 112f, 42f);
			Place(f, save, 608f, 434f, 132f, 42f);
			reset.Click += delegate
			{
				ovStyle = new OverlayStyle();
				label.Box.Text = "";
				changed();
			};
			preview.Click += delegate
			{
				string d = OverlayUrl("delay?demo=on");
				string v = OverlayUrl("viewers?demo=1");
				if (d != null)
				{
					Http.OpenBrowser(d);
					Http.OpenBrowser(v);
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
				AppendLog("Estilo de los overlays guardado.");
				f.Close();
			};
			f.FormClosing += delegate
			{
				if (!saved)
				{
					ovStyle = before;
				}
			};
			changed();
			f.ShowDialog(this);
		}
	}
}
