// Base de los controles dibujados a mano (botones, interruptores, campos…): fondo transparente,
// doble búfer y estado del ratón (hover = encima, pressed = pulsado) para dibujarse distinto.
// Animaciones suaves: HoverT y PressT van de 0 a 1 en un instante (en vez de cambiar de golpe) y
// cada control puede añadir las suyas con Animate. Un solo reloj para toda la ventana, que solo
// funciona mientras algo se está moviendo.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class VControl : Control
	{
		protected bool hover;

		protected bool pressed;

		// 0 = normal, 1 = con el ratón encima / pulsado (con transición).
		protected float hoverT;

		protected float pressT;

		// Lo que tarda cada transición, en segundos.
		private const float HoverSec = 0.14f;

		private const float PressSec = 0.08f;

		private static readonly List<VControl> active = new List<VControl>();

		private static Timer clock;

		private static DateTime lastTick;

		public VControl()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
			BackColor = Color.Transparent;
			ForeColor = Theme.Text;
		}

		// Pone el control en marcha: el reloj lo irá moviendo hasta que Animate diga que ha terminado.
		protected void StartAnimation()
		{
			if (!active.Contains(this))
			{
				active.Add(this);
			}
			if (clock == null)
			{
				clock = new Timer
				{
					Interval = 15
				};
				clock.Tick += Tick;
			}
			if (!clock.Enabled)
			{
				lastTick = DateTime.UtcNow;
				clock.Start();
			}
		}

		private static void Tick(object sender, EventArgs e)
		{
			DateTime now = DateTime.UtcNow;
			float dt = Math.Min(0.05f, (float)(now - lastTick).TotalSeconds);
			lastTick = now;
			foreach (VControl c in active.ToArray())
			{
				bool more = false;
				if (!c.IsDisposed)
				{
					more = c.Animate(dt);
					c.Invalidate();
				}
				if (!more)
				{
					active.Remove(c);
				}
			}
			if (active.Count == 0)
			{
				clock.Stop();
			}
		}

		// Un paso de dt segundos. Devuelve true si aún queda movimiento. Los que añadan animaciones
		// propias lo sobrescriben y llaman a base.Animate.
		protected virtual bool Animate(float dt)
		{
			float hoverTarget = (hover && base.Enabled) ? 1f : 0f;
			float pressTarget = pressed ? 1f : 0f;
			hoverT = Approach(hoverT, hoverTarget, dt / HoverSec);
			pressT = Approach(pressT, pressTarget, dt / PressSec);
			return hoverT != hoverTarget || pressT != pressTarget;
		}

		protected static float Approach(float v, float target, float step)
		{
			if (v < target)
			{
				return Math.Min(target, v + step);
			}
			return Math.Max(target, v - step);
		}

		// Curva suave (empieza y acaba despacio).
		protected static float Ease(float t)
		{
			t = Math.Max(0f, Math.Min(1f, t));
			return t * t * (3f - 2f * t);
		}

		// Mezcla de dos colores según HoverT (ya suavizado).
		protected Color OnHover(Color normal, Color over)
		{
			return Theme.Mix(normal, over, Ease(hoverT));
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			hover = true;
			StartAnimation();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hover = false;
			pressed = false;
			StartAnimation();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				pressed = true;
				StartAnimation();
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			pressed = false;
			StartAnimation();
			base.OnMouseUp(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			StartAnimation();
			Invalidate();
			base.OnEnabledChanged(e);
		}

		protected override void OnGotFocus(EventArgs e)
		{
			Invalidate();
			base.OnGotFocus(e);
		}

		protected override void OnLostFocus(EventArgs e)
		{
			Invalidate();
			base.OnLostFocus(e);
		}

		protected override void Dispose(bool disposing)
		{
			active.Remove(this);
			base.Dispose(disposing);
		}
	}
}
