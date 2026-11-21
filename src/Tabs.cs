// Barra de pestañas de la ventana (Directo, Chat, Overlays…): todas del mismo ancho, con la
// elegida resaltada. MainForm.TabPages dice qué página enseña cada una.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class Tabs : VControl
	{
		public string[] Items = new string[0];

		private int sel;

		private int hot = -1;

		public int Selected
		{
			get
			{
				return sel;
			}
			set
			{
				if (sel != value)
				{
					sel = value;
					Invalidate();
					if (this.SelectedChanged != null)
					{
						this.SelectedChanged(this, EventArgs.Empty);
					}
				}
			}
		}

		public event EventHandler SelectedChanged;

		public Tabs()
		{
			Cursor = Cursors.Hand;
			base.TabStop = false;
		}

		// Celdas de las pestañas: cada una mide lo que su texto (en el idioma de la ventana) más
		// un reparto igual del sitio que sobra, para que quepan 8 aunque unas sean más largas.
		private RectangleF[] Cells()
		{
			float pad = Theme.S(4f);
			RectangleF[] cells = new RectangleF[Items.Length];
			if (Items.Length == 0)
			{
				return cells;
			}
			float[] widths = new float[Items.Length];
			float total = 0f;
			// (Theme guarda las fuentes: no se liberan.)
			Font font = Theme.BodySemi(8.5f);
			for (int i = 0; i < Items.Length; i++)
			{
				widths[i] = TextRenderer.MeasureText(I18n.Tr(Items[i]), font).Width;
				total += widths[i];
			}
			float room = (float)base.Width - 2f * pad;
			float extra = (room - total) / (float)Items.Length;
			float x = pad;
			for (int i = 0; i < Items.Length; i++)
			{
				// Si ni así cabe, todas iguales.
				float w = (extra >= 0f) ? (widths[i] + extra) : (room / (float)Items.Length);
				cells[i] = new RectangleF(x, pad, w, (float)base.Height - 2f * pad);
				x += w;
			}
			return cells;
		}

		// Pestaña que hay en la posición x (-1 si no hay pestañas).
		private int At(int x)
		{
			RectangleF[] cells = Cells();
			for (int i = 0; i < cells.Length; i++)
			{
				if ((float)x < cells[i].Right || i == cells.Length - 1)
				{
					return i;
				}
			}
			return -1;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int tab = At(e.X);
			if (tab != hot)
			{
				hot = tab;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hot = -1;
			base.OnMouseLeave(e);
		}

		protected override void OnMouseClick(MouseEventArgs e)
		{
			Selected = At(e.X);
			base.OnMouseClick(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Theme.Quality(graphics);
			RectangleF r = new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f);
			using (GraphicsPath path = Theme.Round(r, r.Height / 2f))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(230, Theme.Surface)))
				{
					graphics.FillPath(brush, path);
				}
				using (Pen pen = new Pen(Theme.Border))
				{
					graphics.DrawPath(pen, path);
				}
			}
			RectangleF[] cells = Cells();
			for (int i = 0; i < Items.Length; i++)
			{
				RectangleF cell = cells[i];
				if (i == sel)
				{
					using (GraphicsPath path2 = Theme.Round(cell, cell.Height / 2f))
					{
						using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(34, Theme.Violet)))
						{
							graphics.FillPath(brush2, path2);
						}
						using (Pen pen2 = new Pen(Color.FromArgb(120, Theme.Violet)))
						{
							graphics.DrawPath(pen2, path2);
						}
					}
				}
				Color c = ((i == sel) ? Theme.VioletPale : ((i == hot) ? Theme.Text : Theme.Muted));
				Theme.DrawText(graphics, Items[i], Theme.BodySemi(8.5f), c, cell, StringAlignment.Center, StringAlignment.Center);
			}
		}
	}
}
