// Texto con emojis EN COLOR. GDI y GDI+ (lo que usa WinForms) solo saben dibujar los emojis como
// contornos grises; Direct2D + DirectWrite (Windows 8.1 y posteriores) los dibujan como en el
// navegador. Aquí solo lo justo para pintar una línea de texto sobre un HDC.
// Las llamadas van por la tabla de métodos de cada interfaz COM (el número de cada método es su
// posición en la interfaz de d2d1.h / dwrite.h). Si algo falla (Windows antiguo), Available = false
// y quien lo use dibuja como siempre.
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace StreamDelay
{
	internal static class ColorText
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct D2DColor
		{
			public float R, G, B, A;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct D2DRectF
		{
			public float Left, Top, Right, Bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct Rect32
		{
			public int Left, Top, Right, Bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RenderTargetProps
		{
			public int Type;          // D2D1_RENDER_TARGET_TYPE_DEFAULT
			public int Format;        // DXGI_FORMAT_B8G8R8A8_UNORM = 87
			public int AlphaMode;     // D2D1_ALPHA_MODE_PREMULTIPLIED = 1
			public float DpiX, DpiY;
			public int Usage;
			public int MinLevel;
		}

		[DllImport("d2d1.dll")]
		private static extern int D2D1CreateFactory(int type, ref Guid riid, IntPtr options, out IntPtr factory);

		[DllImport("dwrite.dll")]
		private static extern int DWriteCreateFactory(int type, ref Guid riid, out IntPtr factory);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int CreateDCRenderTargetFn(IntPtr self, ref RenderTargetProps props, out IntPtr rt);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int BindDCFn(IntPtr self, IntPtr hdc, ref Rect32 rect);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate void BeginDrawFn(IntPtr self);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int EndDrawFn(IntPtr self, IntPtr tag1, IntPtr tag2);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int CreateSolidColorBrushFn(IntPtr self, ref D2DColor color, IntPtr props, out IntPtr brush);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate void DrawTextFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string text, int length, IntPtr format, ref D2DRectF rect, IntPtr brush, int options, int measuring);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate void SetTextAntialiasModeFn(IntPtr self, int mode);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int CreateTextFormatFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string family, IntPtr collection, int weight, int style, int stretch, float size, [MarshalAs(UnmanagedType.LPWStr)] string locale, out IntPtr format);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate int SetIntFn(IntPtr self, int value);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate uint ReleaseFn(IntPtr self);

		private static IntPtr d2d;

		private static IntPtr dwrite;

		private static bool tried;

		private static bool ok;

		// Por qué falló el último dibujo (para el registro si algo va mal).
		public static string LastError = "";

		// D2D1_DRAW_TEXT_OPTIONS_CLIP (2) | D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT (4).
		private const int DrawOptions = 6;

		private static T Method<T>(IntPtr obj, int index) where T : class
		{
			IntPtr vtable = Marshal.ReadIntPtr(obj);
			IntPtr fn = Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
			return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
		}

		private static void Release(IntPtr obj)
		{
			if (obj != IntPtr.Zero)
			{
				Method<ReleaseFn>(obj, 2)(obj);
			}
		}

		public static bool Available
		{
			get
			{
				if (!tried)
				{
					tried = true;
					try
					{
						// (No se mira la versión de Windows: sin manifiesto dice «Windows 8» aunque sea 11.
						// En Windows sin emojis en color, simplemente salen sin color.)
						Guid factoryId = new Guid("06152247-6f50-465a-9245-118bfd3b6007");
						Guid writeId = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
						ok = D2D1CreateFactory(0, ref factoryId, IntPtr.Zero, out d2d) == 0 && DWriteCreateFactory(0, ref writeId, out dwrite) == 0;
					}
					catch
					{
						ok = false;
					}
				}
				return ok;
			}
		}

		// ¿Lleva emojis? (pares suplentes, símbolos como ⌛ ☀ ❤ y el selector de emoji U+FE0F).
		public static bool HasEmoji(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}
			foreach (char ch in text)
			{
				if (char.IsSurrogate(ch) || (ch >= '\u2300' && ch <= '\u2BFF') || ch == '\uFE0F')
				{
					return true;
				}
			}
			return false;
		}

		// Dibuja una línea de texto (sin saltos; lo que no cabe se corta) en r, en el HDC del Graphics.
		// size en píxeles; h y v: alineación como en StringFormat. Devuelve false si no ha podido
		// (entonces hay que dibujarlo de otra forma).
		public static bool Draw(Graphics g, string text, string family, float size, bool bold, Color color, Rectangle r)
		{
			return Draw(g, text, family, size, bold, color, r, StringAlignment.Near, StringAlignment.Center);
		}

		public static bool Draw(Graphics g, string text, string family, float size, bool bold, Color color, Rectangle r, StringAlignment h, StringAlignment v)
		{
			if (!Available || string.IsNullOrEmpty(text) || r.Width <= 0 || r.Height <= 0)
			{
				return Available;
			}
			IntPtr rt = IntPtr.Zero;
			IntPtr format = IntPtr.Zero;
			IntPtr brush = IntPtr.Zero;
			IntPtr hdc = IntPtr.Zero;
			try
			{
				RenderTargetProps props = new RenderTargetProps
				{
					Type = 0,
					Format = 87,
					AlphaMode = 1,
					DpiX = 96f,
					DpiY = 96f
				};
				if (Method<CreateDCRenderTargetFn>(d2d, 16)(d2d, ref props, out rt) != 0)
				{
					LastError = "CreateDCRenderTarget";
					return false;
				}
				if (Method<CreateTextFormatFn>(dwrite, 15)(dwrite, family, IntPtr.Zero, bold ? 600 : 400, 0, 5, size, "", out format) != 0)
				{
					LastError = "CreateTextFormat";
					return false;
				}
				// Una sola línea, cortada con el recuadro. DirectWrite: 0 = al principio, 1 = al final,
				// 2 = centrado (StringAlignment: Near 0, Center 1, Far 2).
				Method<SetIntFn>(format, 5)(format, 1);   // SetWordWrapping: NO_WRAP
				Method<SetIntFn>(format, 3)(format, (h == StringAlignment.Center) ? 2 : ((h == StringAlignment.Far) ? 1 : 0));   // SetTextAlignment
				Method<SetIntFn>(format, 4)(format, (v == StringAlignment.Center) ? 2 : ((v == StringAlignment.Far) ? 1 : 0));   // SetParagraphAlignment
				D2DColor c = new D2DColor
				{
					R = (float)(int)color.R / 255f,
					G = (float)(int)color.G / 255f,
					B = (float)(int)color.B / 255f,
					A = (float)(int)color.A / 255f
				};
				hdc = g.GetHdc();
				Rect32 bind = new Rect32
				{
					Left = r.Left,
					Top = r.Top,
					Right = r.Right,
					Bottom = r.Bottom
				};
				if (Method<BindDCFn>(rt, 57)(rt, hdc, ref bind) != 0)
				{
					LastError = "BindDC";
					return false;
				}
				Method<BeginDrawFn>(rt, 48)(rt);
				Method<SetTextAntialiasModeFn>(rt, 34)(rt, 2);   // GRAYSCALE (sobre fondo transparente)
				if (Method<CreateSolidColorBrushFn>(rt, 8)(rt, ref c, IntPtr.Zero, out brush) != 0)
				{
					Method<EndDrawFn>(rt, 49)(rt, IntPtr.Zero, IntPtr.Zero);
					LastError = "CreateSolidColorBrush";
					return false;
				}
				D2DRectF box = new D2DRectF
				{
					Left = 0f,
					Top = 0f,
					Right = r.Width,
					Bottom = r.Height
				};
				Method<DrawTextFn>(rt, 27)(rt, text, text.Length, format, ref box, brush, DrawOptions, 0);
				int hr = Method<EndDrawFn>(rt, 49)(rt, IntPtr.Zero, IntPtr.Zero);
				if (hr != 0)
				{
					LastError = "EndDraw 0x" + hr.ToString("X8");
					return false;
				}
				return true;
			}
			catch (Exception ex)
			{
				LastError = ex.GetType().Name + ": " + ex.Message;
				return false;
			}
			finally
			{
				if (hdc != IntPtr.Zero)
				{
					g.ReleaseHdc(hdc);
				}
				Release(brush);
				Release(format);
				Release(rt);
			}
		}
	}
}
