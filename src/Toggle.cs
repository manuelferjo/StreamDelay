using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class Toggle : VControl
	{
		private bool on;

		public string Hint = "";

		public bool Checked
		{
			get
			{
				return on;
			}
			set
			{
				if (on != value)
				{
					on = value;
					Invalidate();
					if (this.CheckedChanged != null)
					{
						this.CheckedChanged(this, EventArgs.Empty);
					}
				}
			}
		}

		public event EventHandler CheckedChanged;

		public Toggle()
		{
			Cursor = Cursors.Hand;
			base.TabStop = true;
		}

		protected override void OnClick(EventArgs e)
		{
			Focus();
			Checked = !Checked;
			base.OnClick(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space)
			{
				Checked = !Checked;
				e.Handled = true;
			}
			base.OnKeyDown(e);
		}

		protected override void OnTextChanged(EventArgs e)
		{
			Invalidate();
			base.OnTextChanged(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Theme.Quality(graphics);
			float num = Theme.S(38f);
			float num2 = Theme.S(22f);
			float num3 = ((Hint.Length > 0) ? ((float)Theme.S(2f)) : (((float)base.Height - num2) / 2f));
			RectangleF rectangleF = new RectangleF(1f, num3, num, num2);
			using (GraphicsPath path = Theme.Round(rectangleF, num2 / 2f))
			{
				if (on)
				{
					using (LinearGradientBrush brush = Theme.Brand(rectangleF, false))
					{
						graphics.FillPath(brush, path);
					}
				}
				else
				{
					using (SolidBrush brush2 = new SolidBrush(hover ? Color.FromArgb(44, 44, 56) : Color.FromArgb(36, 36, 46)))
					{
						graphics.FillPath(brush2, path);
					}
					using (Pen pen = new Pen(Theme.BorderHi))
					{
						graphics.DrawPath(pen, path);
					}
				}
			}
			float num4 = num2 - (float)Theme.S(6f);
			RectangleF rect = new RectangleF(on ? (rectangleF.Right - num4 - (float)Theme.S(3f)) : (rectangleF.X + (float)Theme.S(3f)), num3 + (float)Theme.S(3f), num4, num4);
			using (SolidBrush brush3 = new SolidBrush(on ? Color.FromArgb(14, 14, 18) : Theme.Muted))
			{
				graphics.FillEllipse(brush3, rect);
			}
			if (Focused && ShowFocusCues)
			{
				using (Pen pen2 = new Pen(Color.FromArgb(160, Theme.Violet), 1.5f))
				{
					RectangleF r = RectangleF.Inflate(rectangleF, 2f, 2f);
					using (GraphicsPath path2 = Theme.Round(r, r.Height / 2f))
					{
						graphics.DrawPath(pen2, path2);
					}
				}
			}
			float num5 = num + (float)Theme.S(12f);
			if (Hint.Length == 0)
			{
				Theme.DrawText(graphics, Text, Theme.BodySemi(9f), base.Enabled ? Theme.Text : Theme.Dim, new RectangleF(num5, 0f, (float)base.Width - num5, base.Height), StringAlignment.Near, StringAlignment.Center);
				return;
			}
			Theme.DrawText(graphics, Text, Theme.BodySemi(9f), Theme.Text, new RectangleF(num5, 0f, (float)base.Width - num5, Theme.S(24f)), StringAlignment.Near, StringAlignment.Center);
			Theme.DrawText(graphics, Hint, Theme.Body(8f), Theme.Muted, new RectangleF(num5, Theme.S(22f), (float)base.Width - num5, base.Height - Theme.S(22f)), StringAlignment.Near, StringAlignment.Near);
		}
	}
}
