using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class VButton : VControl
	{
		public ButtonKind Kind = ButtonKind.Ghost;

		public bool Selected;

		public bool Muted;

		public float FontSize = 9.5f;

		public VButton()
		{
			Cursor = Cursors.Hand;
			base.TabStop = true;
		}

		protected override void OnTextChanged(EventArgs e)
		{
			Invalidate();
			base.OnTextChanged(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
			{
				OnClick(EventArgs.Empty);
				e.Handled = true;
			}
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Theme.Quality(graphics);
			RectangleF rectangleF = new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f);
			if (pressed)
			{
				rectangleF.Inflate(-1f, -1f);
			}
			float num = ((Kind == ButtonKind.Pill) ? (rectangleF.Height / 2f) : ((float)Theme.S(12f)));
			Color c = Theme.Text;
			Font f = ((Kind == ButtonKind.Pill) ? Theme.Mono(FontSize) : Theme.BodyBold(FontSize));
			ButtonKind buttonKind = ((!base.Enabled && (Kind == ButtonKind.Danger || Kind == ButtonKind.Ghost)) ? ButtonKind.Ghost : Kind);
			using (GraphicsPath path = Theme.Round(rectangleF, num))
			{
				switch (buttonKind)
				{
				case ButtonKind.Primary:
				{
					if (!base.Enabled || Muted)
					{
						using (SolidBrush brush5 = new SolidBrush(Theme.Elevated))
						{
							graphics.FillPath(brush5, path);
						}
						using (Pen pen5 = new Pen(Theme.Border))
						{
							graphics.DrawPath(pen5, path);
						}
						c = (base.Enabled ? Theme.Muted : Theme.Dim);
						break;
					}
					if (hover)
					{
						for (int num2 = 3; num2 >= 1; num2--)
						{
							RectangleF r = RectangleF.Inflate(rectangleF, num2 * 2, num2 * 2);
							using (GraphicsPath path2 = Theme.Round(r, num + (float)(num2 * 2)))
							{
								using (SolidBrush brush6 = new SolidBrush(Color.FromArgb(18, Theme.Violet)))
								{
									graphics.FillPath(brush6, path2);
								}
							}
						}
					}
					using (LinearGradientBrush brush7 = Theme.Brand(rectangleF, false))
					{
						graphics.FillPath(brush7, path);
					}
					c = Color.Black;
					break;
				}
				case ButtonKind.Danger:
				{
					using (SolidBrush brush2 = new SolidBrush(hover ? Color.FromArgb(40, Theme.Red) : Color.FromArgb(18, Theme.Red)))
					{
						graphics.FillPath(brush2, path);
					}
					using (Pen pen2 = new Pen(Color.FromArgb(hover ? 230 : 150, Theme.Red), 1.2f))
					{
						graphics.DrawPath(pen2, path);
					}
					c = Color.FromArgb(255, 120, 130);
					break;
				}
				case ButtonKind.Pill:
					if (Selected)
					{
						using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(base.Enabled ? 38 : 20, Theme.Violet)))
						{
							graphics.FillPath(brush3, path);
						}
						using (Pen pen3 = new Pen(Color.FromArgb(base.Enabled ? 200 : 90, Theme.Violet), 1.2f))
						{
							graphics.DrawPath(pen3, path);
						}
						c = (base.Enabled ? Theme.VioletLight : Theme.Mix(Theme.VioletLight, Theme.Bg, 0.4f));
					}
					else
					{
						using (SolidBrush brush4 = new SolidBrush((hover && base.Enabled) ? Theme.Elevated : Theme.Input))
						{
							graphics.FillPath(brush4, path);
						}
						using (Pen pen4 = new Pen((hover && base.Enabled) ? Theme.BorderHi : Theme.Border))
						{
							graphics.DrawPath(pen4, path);
						}
						c = ((!base.Enabled) ? Theme.Dim : (hover ? Theme.Text : Theme.Muted));
					}
					break;
				case ButtonKind.Link:
					c = (hover ? Theme.VioletLight : Theme.Violet);
					f = Theme.BodySemi(FontSize);
					break;
				default:
				{
					using (SolidBrush brush = new SolidBrush(hover ? Color.FromArgb(32, 32, 42) : Theme.Elevated))
					{
						graphics.FillPath(brush, path);
					}
					using (Pen pen = new Pen(hover ? Theme.BorderHi : Theme.Border))
					{
						graphics.DrawPath(pen, path);
					}
					if (!base.Enabled)
					{
						c = Theme.Dim;
					}
					break;
				}
				}
				if (Focused && ShowFocusCues && Kind != ButtonKind.Link)
				{
					Pen pen6 = new Pen(Color.FromArgb(160, Theme.Violet), 1.5f);
					pen6.DashStyle = DashStyle.Dot;
					using (Pen pen7 = pen6)
					{
						RectangleF r2 = RectangleF.Inflate(rectangleF, -3f, -3f);
						using (GraphicsPath path3 = Theme.Round(r2, Math.Max(2f, num - 3f)))
						{
							graphics.DrawPath(pen7, path3);
						}
					}
				}
			}
			Theme.DrawText(graphics, Text, f, c, rectangleF, (Kind != ButtonKind.Link) ? StringAlignment.Center : StringAlignment.Far, StringAlignment.Center);
		}
	}
}
