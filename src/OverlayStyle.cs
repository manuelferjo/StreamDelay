// Estilo de los overlays (delay, espectadores y chat en pantalla): colores, fondo, forma, borde,
// brillo, fuente y algunos detalles. Es uno para los tres, así todo va con la misma marca.
// Las páginas lo leen de /style.json cada 2 s, así que los cambios se ven al momento en OBS.
// Se guarda en StreamDelay.ini (ov_style=, en Base64).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace StreamDelay
{
	internal class OverlayStyle
	{
		// Colores en "#RRGGBB".
		// Por defecto, el rojo de siempre ("en directo").
		public string Accent = "#FF4655";

		public string Accent2 = "#FF8A94";

		// Degradado del primer color al segundo (si no, solo el primero).
		public bool Gradient = true;

		public string Background = "#111116";

		// Opacidad del fondo, de 0 (transparente) a 100.
		public int Opacity = 92;

		public string Text = "#F4F4F5";

		// "round" redondeada, "pill" píldora, "rect" recta, "circle" círculo (el de delay; el resto, píldora).
		public string Shape = "round";

		// Grosor del borde en píxeles (0 = sin borde).
		public int Border = 2;

		public bool Glow = true;

		public string Font = "Outfit";

		public bool Icon = true;

		// Texto pequeño del overlay de delay (vacío = "Delay").
		public string Label = "";

		// Fondo detrás de cada mensaje del chat en pantalla.
		public bool ChatBubbles;

		public static readonly string[] Fonts = new string[12]
		{
			"Outfit", "Montserrat", "Poppins", "Inter", "Bebas Neue", "Oswald",
			"Orbitron", "Russo One", "Rubik", "Bangers", "Permanent Marker", "Press Start 2P"
		};

		public static readonly string[] PresetNames = new string[8] { "Directo", "StreamDelay", "Neón", "Fuego", "Hielo", "Minimal", "Retro", "Claro" };

		// Estilos listos para usar.
		public static OverlayStyle Preset(int i)
		{
			switch (i)
			{
			case 1:
				return new OverlayStyle { Accent = "#C09AFD", Accent2 = "#EDE103", Shape = "round", Border = 2, Font = "Outfit" };
			case 2:
				return new OverlayStyle { Accent = "#00F0FF", Accent2 = "#FF00E5", Background = "#05050A", Opacity = 85, Text = "#FFFFFF", Shape = "pill", Border = 3, Font = "Orbitron" };
			case 3:
				return new OverlayStyle { Accent = "#FF4D00", Accent2 = "#FFC400", Background = "#140600", Opacity = 90, Text = "#FFF4E5", Shape = "round", Border = 2, Font = "Bebas Neue" };
			case 4:
				return new OverlayStyle { Accent = "#7DD3FC", Accent2 = "#E0F2FE", Background = "#0B1220", Opacity = 85, Text = "#F0F9FF", Shape = "pill", Border = 2, Font = "Poppins" };
			case 5:
				return new OverlayStyle { Accent = "#FFFFFF", Accent2 = "#FFFFFF", Gradient = false, Background = "#000000", Opacity = 60, Text = "#FFFFFF", Shape = "rect", Border = 0, Glow = false, Font = "Inter" };
			case 6:
				return new OverlayStyle { Accent = "#39FF14", Accent2 = "#39FF14", Gradient = false, Background = "#000000", Opacity = 90, Text = "#39FF14", Shape = "rect", Border = 3, Glow = false, Font = "Press Start 2P" };
			case 7:
				return new OverlayStyle { Accent = "#6D28D9", Accent2 = "#DB2777", Background = "#FFFFFF", Opacity = 95, Text = "#111111", Shape = "round", Border = 2, Glow = false, Font = "Montserrat" };
			default:
				return new OverlayStyle();
			}
		}

		// Copia de los valores de "Estilos listos" que se ven (el texto del delay y el fondo del chat se mantienen).
		public void ApplyPreset(OverlayStyle p)
		{
			Accent = p.Accent;
			Accent2 = p.Accent2;
			Gradient = p.Gradient;
			Background = p.Background;
			Opacity = p.Opacity;
			Text = p.Text;
			Shape = p.Shape;
			Border = p.Border;
			Glow = p.Glow;
			Font = p.Font;
		}

		public OverlayStyle Clone()
		{
			return (OverlayStyle)MemberwiseClone();
		}

		public Dictionary<string, object> ToDict()
		{
			Dictionary<string, object> d = new Dictionary<string, object>();
			d["a1"] = Accent;
			d["a2"] = Accent2;
			d["grad"] = Gradient;
			d["bg"] = Background;
			d["op"] = Opacity;
			d["tx"] = Text;
			d["shape"] = Shape;
			d["bd"] = Border;
			d["glow"] = Glow;
			d["font"] = Font;
			d["icon"] = Icon;
			d["label"] = Label;
			d["bubbles"] = ChatBubbles;
			return d;
		}

		public string ToJson()
		{
			return Web.ToJson(ToDict());
		}

		// Para StreamDelay.ini (una línea).
		public string Save()
		{
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(ToJson()));
		}

		public static OverlayStyle Load(string saved)
		{
			OverlayStyle s = new OverlayStyle();
			try
			{
				object o = Web.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(saved)));
				s.Accent = Color(J.S(o, "a1"), s.Accent);
				s.Accent2 = Color(J.S(o, "a2"), s.Accent2);
				s.Gradient = J.S(o, "grad") != "False";
				s.Background = Color(J.S(o, "bg"), s.Background);
				s.Opacity = (int)Math.Max(0L, Math.Min(100L, J.N(o, s.Opacity, "op")));
				s.Text = Color(J.S(o, "tx"), s.Text);
				string shape = J.S(o, "shape");
				if (shape == "round" || shape == "pill" || shape == "rect" || shape == "circle")
				{
					s.Shape = shape;
				}
				s.Border = (int)Math.Max(0L, Math.Min(6L, J.N(o, s.Border, "bd")));
				s.Glow = J.S(o, "glow") != "False";
				string font = J.S(o, "font");
				if (Array.IndexOf(Fonts, font) >= 0)
				{
					s.Font = font;
				}
				s.Icon = J.S(o, "icon") != "False";
				s.Label = J.S(o, "label");
				if (s.Label.Length > 24)
				{
					s.Label = s.Label.Substring(0, 24);
				}
				s.ChatBubbles = J.S(o, "bubbles") == "True";
			}
			catch
			{
			}
			return s;
		}

		// Solo se aceptan colores "#RRGGBB".
		private static string Color(string v, string def)
		{
			if (v != null && v.Length == 7 && v[0] == '#')
			{
				int n;
				if (int.TryParse(v.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out n))
				{
					return v.ToUpperInvariant();
				}
			}
			return def;
		}

		public static Color ToColor(string hex)
		{
			try
			{
				return ColorTranslator.FromHtml(hex);
			}
			catch
			{
				return System.Drawing.Color.Gray;
			}
		}

		public static string ToHex(Color c)
		{
			return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
		}
	}
}
