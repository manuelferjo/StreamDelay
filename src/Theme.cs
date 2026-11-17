using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace StreamDelay
{
	internal static class Theme
	{
		public static readonly Color Bg = Color.FromArgb(9, 9, 12);

		public static readonly Color Surface = Color.FromArgb(17, 17, 22);

		public static readonly Color Elevated = Color.FromArgb(23, 23, 32);

		public static readonly Color Input = Color.FromArgb(14, 14, 19);

		public static readonly Color Border = Color.FromArgb(38, 38, 46);

		public static readonly Color BorderHi = Color.FromArgb(56, 54, 70);

		public static readonly Color Text = Color.FromArgb(244, 244, 245);

		public static readonly Color Muted = Color.FromArgb(161, 161, 170);

		public static readonly Color Dim = Color.FromArgb(113, 113, 122);

		public static readonly Color Violet = Color.FromArgb(192, 154, 253);

		public static readonly Color VioletLight = Color.FromArgb(214, 191, 255);

		public static readonly Color VioletPale = Color.FromArgb(227, 213, 255);

		public static readonly Color Yellow = Color.FromArgb(237, 225, 3);

		public static readonly Color Red = Color.FromArgb(255, 70, 85);

		public static readonly Color Green = Color.FromArgb(52, 211, 153);

		public static float Scale = 1f;

		private static readonly PrivateFontCollection pfc = new PrivateFontCollection();

		private static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

		public static int S(float v)
		{
			return (int)Math.Round(v * Scale);
		}

		public static void Init()
		{
			try
			{
				using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
				{
					Scale = graphics.DpiX / 96f;
				}
			}
			catch
			{
				Scale = 1f;
			}
			if (Scale < 1f)
			{
				Scale = 1f;
			}
			Assembly assembly = typeof(Theme).Assembly;
			string[] manifestResourceNames = assembly.GetManifestResourceNames();
			foreach (string text in manifestResourceNames)
			{
				if (!text.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				try
				{
					byte[] array;
					using (Stream stream = assembly.GetManifestResourceStream(text))
					{
						array = new byte[stream.Length];
						int num;
						for (int j = 0; j < array.Length; j += num)
						{
							num = stream.Read(array, j, array.Length - j);
							if (num <= 0)
							{
								break;
							}
						}
					}
					IntPtr intPtr = Marshal.AllocCoTaskMem(array.Length);
					Marshal.Copy(array, 0, intPtr, array.Length);
					pfc.AddMemoryFont(intPtr, array.Length);
				}
				catch
				{
				}
			}
		}

		private static FontFamily Family(string name)
		{
			FontFamily[] families = pfc.Families;
			foreach (FontFamily fontFamily in families)
			{
				if (string.Equals(fontFamily.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return fontFamily;
				}
			}
			return null;
		}

		private static Font Make(string family, float size, FontStyle style, string fallback)
		{
			string key = family + "|" + size + "|" + (int)style;
			Font value;
			if (cache.TryGetValue(key, out value))
			{
				return value;
			}
			float emSize = size * 96f / 72f * Scale;
			try
			{
				FontFamily fontFamily = Family(family);
				if (fontFamily != null)
				{
					FontStyle style2 = style;
					if (!fontFamily.IsStyleAvailable(style2))
					{
						style2 = ((!fontFamily.IsStyleAvailable(FontStyle.Regular)) ? FontStyle.Bold : FontStyle.Regular);
					}
					value = new Font(fontFamily, emSize, style2, GraphicsUnit.Pixel);
				}
			}
			catch
			{
				value = null;
			}
			if (value == null)
			{
				value = new Font(fallback, emSize, (style != FontStyle.Regular) ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
			}
			cache[key] = value;
			return value;
		}

		public static Font Display(float size)
		{
			return Make("Outfit", size, FontStyle.Bold, "Segoe UI Semibold");
		}

		public static Font Body(float size)
		{
			return Make("Plus Jakarta Sans", size, FontStyle.Regular, "Segoe UI");
		}

		public static Font BodySemi(float size)
		{
			return Make("Plus Jakarta Sans SemiBold", size, FontStyle.Regular, "Segoe UI Semibold");
		}

		public static Font BodyBold(float size)
		{
			return Make("Plus Jakarta Sans", size, FontStyle.Bold, "Segoe UI Semibold");
		}

		public static Font Mono(float size)
		{
			return Make("JetBrains Mono Medium", size, FontStyle.Regular, "Consolas");
		}

		public static Font Native(float size)
		{
			return Make("", size, FontStyle.Regular, "Segoe UI");
		}

		public static GraphicsPath Round(RectangleF r, float radius)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			float num = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
			if (num <= 1f)
			{
				graphicsPath.AddRectangle(r);
				return graphicsPath;
			}
			graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}

		public static LinearGradientBrush Brand(RectangleF r, bool withPale)
		{
			if (r.Width < 1f)
			{
				r.Width = 1f;
			}
			if (r.Height < 1f)
			{
				r.Height = 1f;
			}
			LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new RectangleF(r.X - 1f, r.Y - 1f, r.Width + 2f, r.Height + 2f), Violet, Yellow, 20f);
			if (withPale)
			{
				ColorBlend colorBlend = new ColorBlend(3);
				colorBlend.Colors = new Color[3] { Violet, VioletPale, Yellow };
				colorBlend.Positions = new float[3] { 0f, 0.45f, 1f };
				linearGradientBrush.InterpolationColors = colorBlend;
			}
			return linearGradientBrush;
		}

		public static void Quality(Graphics g)
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		}

		public static void DrawText(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
		{
			using (StringFormat stringFormat = new StringFormat())
			{
				using (SolidBrush brush = new SolidBrush(c))
				{
					stringFormat.Alignment = h;
					stringFormat.LineAlignment = v;
					stringFormat.Trimming = StringTrimming.EllipsisCharacter;
					stringFormat.FormatFlags = StringFormatFlags.NoWrap;
					g.DrawString(s, f, brush, r, stringFormat);
				}
			}
		}

		public static void DrawWrapped(Graphics g, string s, Font f, Color c, RectangleF r)
		{
			using (StringFormat stringFormat = new StringFormat())
			{
				using (SolidBrush brush = new SolidBrush(c))
				{
					stringFormat.Trimming = StringTrimming.EllipsisWord;
					g.DrawString(s, f, brush, r, stringFormat);
				}
			}
		}

		public static SizeF Measure(Graphics g, string s, Font f)
		{
			return g.MeasureString(s, f, new PointF(0f, 0f), StringFormat.GenericTypographic);
		}

		public static float Ascent(Graphics g, Font f)
		{
			FontFamily fontFamily = f.FontFamily;
			float num = ((f.Unit == GraphicsUnit.Pixel) ? f.Size : (f.SizeInPoints * g.DpiY / 72f));
			return num * (float)fontFamily.GetCellAscent(f.Style) / (float)fontFamily.GetEmHeight(f.Style);
		}

		public static Color Mix(Color a, Color b, float t)
		{
			return Color.FromArgb((int)((float)(int)a.A + (float)(b.A - a.A) * t), (int)((float)(int)a.R + (float)(b.R - a.R) * t), (int)((float)(int)a.G + (float)(b.G - a.G) * t), (int)((float)(int)a.B + (float)(b.B - a.B) * t));
		}

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

		public static void DarkTitleBar(IntPtr hwnd)
		{
			try
			{
				int value = 1;
				if (DwmSetWindowAttribute(hwnd, 20, ref value, 4) != 0)
				{
					DwmSetWindowAttribute(hwnd, 19, ref value, 4);
				}
				int value2 = Bg.R | (Bg.G << 8) | (Bg.B << 16);
				DwmSetWindowAttribute(hwnd, 35, ref value2, 4);
				int value3 = 3811891;
				DwmSetWindowAttribute(hwnd, 34, ref value3, 4);
			}
			catch
			{
			}
		}
	}
}
