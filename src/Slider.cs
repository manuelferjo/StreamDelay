using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class Slider : VControl
	{
		private int value;

		private int max = 600;

		private bool dragging;

		public int Maximum
		{
			get
			{
				return max;
			}
			set
			{
				max = Math.Max(1, value);
				Invalidate();
			}
		}

		public int Value
		{
			get
			{
				return value;
			}
			set
			{
				int num = Math.Max(0, Math.Min(max, value));
				if (num != this.value)
				{
					this.value = num;
					Invalidate();
					if (this.ValueChanged != null)
					{
						this.ValueChanged(this, EventArgs.Empty);
					}
				}
			}
		}

		private float Pad
		{
			get
			{
				return Theme.S(11f);
			}
		}

		public event EventHandler ValueChanged;

		public Slider()
		{
			Cursor = Cursors.Hand;
			base.TabStop = true;
		}

		private float PosOf(int v)
		{
			return (float)Math.Sqrt((double)v / (double)max);
		}

		private int ValOf(float pos)
		{
			pos = Math.Max(0f, Math.Min(1f, pos));
			return (int)Math.Round(pos * pos * (float)max);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Button == MouseButtons.Left)
			{
				Focus();
				dragging = true;
				Track(e.X);
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (dragging)
			{
				Track(e.X);
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			dragging = false;
		}

		private void Track(int x)
		{
			Value = ValOf(((float)x - Pad) / ((float)base.Width - 2f * Pad));
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			Value += ((e.Delta > 0) ? 1 : (-1));
		}

		protected override bool IsInputKey(Keys k)
		{
			if (k != Keys.Left && k != Keys.Right && k != Keys.Up && k != Keys.Down)
			{
				return base.IsInputKey(k);
			}
			return true;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down)
			{
				Value--;
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up)
			{
				Value++;
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.PageUp)
			{
				Value += 10;
			}
			else if (e.KeyCode == Keys.Next)
			{
				Value -= 10;
			}
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Theme.Quality(graphics);
			float num = (float)base.Height / 2f;
			float num2 = Theme.S(6f);
			RectangleF r = new RectangleF(Pad, num - num2 / 2f, (float)base.Width - 2f * Pad, num2);
			using (GraphicsPath path = Theme.Round(r, num2 / 2f))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(38, 38, 48)))
				{
					graphics.FillPath(brush, path);
				}
			}
			float num3 = Pad + PosOf(value) * ((float)base.Width - 2f * Pad);
			int[] array = new int[4] { 30, 60, 120, 300 };
			foreach (int v in array)
			{
				float num4 = Pad + PosOf(v) * ((float)base.Width - 2f * Pad);
				using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
				{
					graphics.FillEllipse(brush2, num4 - 1.5f, num - 1.5f, 3f, 3f);
				}
			}
			if (num3 > Pad + 1f)
			{
				RectangleF r2 = new RectangleF(Pad, num - num2 / 2f, num3 - Pad, num2);
				using (GraphicsPath path2 = Theme.Round(r2, num2 / 2f))
				{
					using (LinearGradientBrush brush3 = Theme.Brand(new RectangleF(Pad, 0f, (float)base.Width - 2f * Pad, base.Height), false))
					{
						graphics.FillPath(brush3, path2);
					}
				}
			}
			float num5 = Theme.S(18f);
			if (hover || dragging || Focused)
			{
				Card.Glow(graphics, new RectangleF(num3 - num5, num - num5, 2f * num5, 2f * num5), Color.FromArgb(90, Theme.Violet));
			}
			using (SolidBrush brush4 = new SolidBrush(Theme.Text))
			{
				graphics.FillEllipse(brush4, num3 - num5 / 2f, num - num5 / 2f, num5, num5);
			}
			using (Pen pen = new Pen(Theme.Violet, 2f))
			{
				graphics.DrawEllipse(pen, num3 - num5 / 2f, num - num5 / 2f, num5, num5);
			}
		}
	}
}
