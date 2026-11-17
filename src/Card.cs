using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class Card : VControl
	{
		public string Title = "";

		public string Subtitle = "";

		public bool Accent;

		public Card()
		{
			SetStyle(ControlStyles.Selectable, false);
			base.TabStop = false;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Theme.Quality(graphics);
			RectangleF rectangleF = new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f);
			float radius = Theme.S(18f);
			using (GraphicsPath graphicsPath = Theme.Round(rectangleF, radius))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(225, Theme.Surface)))
				{
					graphics.FillPath(brush, graphicsPath);
				}
				if (Accent)
				{
					graphics.SetClip(graphicsPath);
					Glow(graphics, new RectangleF((float)(-base.Width) * 0.3f, (float)(-base.Height) * 0.6f, (float)base.Width * 0.9f, (float)base.Height * 1.1f), Color.FromArgb(40, Theme.Violet));
					Glow(graphics, new RectangleF((float)base.Width * 0.55f, (float)base.Height * 0.35f, (float)base.Width * 0.7f, (float)base.Height * 1f), Color.FromArgb(26, Theme.Yellow));
					graphics.ResetClip();
					using (LinearGradientBrush brush2 = new LinearGradientBrush(rectangleF, Color.FromArgb(120, Theme.Violet), Color.FromArgb(80, Theme.Yellow), 20f))
					{
						using (Pen pen = new Pen(brush2, 1.2f))
						{
							graphics.DrawPath(pen, graphicsPath);
						}
					}
				}
				else
				{
					using (Pen pen2 = new Pen(Theme.Border, 1f))
					{
						graphics.DrawPath(pen2, graphicsPath);
					}
				}
			}
			if (Title.Length > 0)
			{
				Theme.DrawText(graphics, Title, Theme.Display(11.5f), Theme.Text, new RectangleF(Theme.S(20f), Theme.S(14f), base.Width - Theme.S(40f), Theme.S(24f)), StringAlignment.Near, StringAlignment.Center);
				if (Subtitle.Length > 0)
				{
					Theme.DrawText(graphics, Subtitle, Theme.Body(8.25f), Theme.Muted, new RectangleF(Theme.S(20f), Theme.S(36f), base.Width - Theme.S(40f), Theme.S(18f)), StringAlignment.Near, StringAlignment.Center);
				}
			}
			base.OnPaint(e);
		}

		public static void Glow(Graphics g, RectangleF r, Color c)
		{
			using (GraphicsPath graphicsPath = new GraphicsPath())
			{
				graphicsPath.AddEllipse(r);
				using (PathGradientBrush pathGradientBrush = new PathGradientBrush(graphicsPath))
				{
					pathGradientBrush.CenterColor = c;
					pathGradientBrush.SurroundColors = new Color[1] { Color.FromArgb(0, c) };
					g.FillPath(pathGradientBrush, graphicsPath);
				}
			}
		}
	}
}
