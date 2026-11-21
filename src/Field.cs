// Campo de texto con el tema oscuro: un TextBox normal (Box) dentro de un recuadro redondeado
// que se ilumina en violeta al escribir.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class Field : VControl
	{
		public readonly TextBox Box;

		private bool focused;

		// ReadOnly: siempre de solo lectura (en violeta, como el servidor de OBS).
		private bool fixedRo;

		// Locked: bloqueado por ahora (en gris, como los canales con el chat encendido).
		private bool locked;

		// Emojis en color (como en Twitch o Kick): mientras no se escribe, el texto se dibuja con
		// ColorText en vez del TextBox (que solo sabe ponerlos en gris).
		private bool colorEmoji;

		// Si se enseña el TextBox (Box.Visible también dice false cuando la pestaña está oculta).
		private bool boxShown = true;

		public bool ColorEmoji
		{
			get
			{
				return colorEmoji;
			}
			set
			{
				colorEmoji = value;
				SyncEmoji();
			}
		}

		public bool ReadOnly
		{
			get
			{
				return fixedRo;
			}
			set
			{
				fixedRo = value;
				Sync();
			}
		}

		public bool Locked
		{
			get
			{
				return locked;
			}
			set
			{
				if (locked != value)
				{
					locked = value;
					Sync();
					Invalidate();
				}
			}
		}

		public Field()
		{
			SetStyle(ControlStyles.Selectable, false);
			base.TabStop = false;
			Box = new TextBox
			{
				BorderStyle = BorderStyle.None,
				BackColor = Theme.Input,
				ForeColor = Theme.Text,
				Font = Theme.Native(10f)
			};
			base.Controls.Add(Box);
			TextBox box = Box;
			EventHandler onFocus = delegate
			{
				focused = true;
				Invalidate();
			};
			box.GotFocus += onFocus;
			Box.LostFocus += delegate
			{
				focused = false;
				// Cuando Windows haya terminado de mover el foco.
				if (IsHandleCreated)
				{
					BeginInvoke((Action)SyncEmoji);
				}
				Invalidate();
			};
			Box.TextChanged += delegate
			{
				SyncEmoji();
			};
			Cursor = Cursors.IBeam;
		}

		private void Sync()
		{
			Box.ReadOnly = fixedRo || locked;
			Box.BackColor = Theme.Input;
			Box.ForeColor = (locked ? Theme.Dim : (fixedRo ? Theme.VioletLight : Theme.Text));
		}

		// Con emojis y sin escribir, el TextBox se esconde y se dibuja el texto en color.
		private void SyncEmoji()
		{
			bool show = !colorEmoji || Box.Focused || !ColorText.HasEmoji(Box.Text) || !ColorText.Available;
			if (boxShown != show)
			{
				boxShown = show;
				Box.Visible = show;
			}
			Invalidate();
		}

		// Escribir otra vez: vuelve el TextBox (con el cursor al final).
		public void EditNow()
		{
			if (!boxShown)
			{
				boxShown = true;
				Box.Visible = true;
				Box.SelectionStart = Box.TextLength;
			}
			Box.Focus();
		}

		protected override void OnClick(EventArgs e)
		{
			EditNow();
			base.OnClick(e);
		}

		protected override void OnLayout(LayoutEventArgs e)
		{
			base.OnLayout(e);
			// El TextBox va dentro con 12 de margen (centrado en vertical si es de una línea).
			int pad = Theme.S(12f);
			if (Box.Multiline)
			{
				Box.SetBounds(pad, Theme.S(8f), base.Width - 2 * pad, base.Height - Theme.S(16f));
				return;
			}
			Box.SetBounds(pad, (base.Height - Box.Height) / 2, base.Width - 2 * pad, Box.Height);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Theme.Quality(graphics);
			RectangleF r = new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f);
			using (GraphicsPath path = Theme.Round(r, Theme.S(10f)))
			{
				using (SolidBrush brush = new SolidBrush(Theme.Input))
				{
					graphics.FillPath(brush, path);
				}
				using (Pen pen = new Pen(focused ? Theme.Violet : OnHover(Theme.Border, Theme.BorderHi), focused ? 1.5f : 1f))
				{
					graphics.DrawPath(pen, path);
				}
			}
			if (!boxShown && Box.Text.Length > 0)
			{
				Font font = Box.Font;
				float px = (font.Unit == GraphicsUnit.Pixel) ? font.Size : (font.SizeInPoints * 96f / 72f * Theme.Scale);
				Rectangle area = new Rectangle(Box.Left + 1, 0, Box.Width - 1, base.Height);
				if (!ColorText.Draw(graphics, Box.Text, "Segoe UI", px, false, Box.ForeColor, area))
				{
					boxShown = true;
					Box.Visible = true;
				}
			}
		}
	}
}
