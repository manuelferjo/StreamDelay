// Estilo del chat (pestaña Overlays → Chat en pantalla → Personalizar chat →): fuente, tamaño,
// colores, fondo (sin fondo, detrás de cada mensaje o de todo el chat), efecto del texto, qué
// se enseña, cuánto dura cada mensaje y su animación. Es aparte del estilo de los demás overlays.
// La página del chat lo lee de /chatstyle.json cada 2 s, así que los cambios se ven al momento en OBS.
// Se aplica al chat en pantalla y, si se elige, también al panel del chat (OBS o navegador).
// Se guarda en VortexsLive.ini (chat_style=, en Base64).
using System;
using System.Collections.Generic;
using System.Text;

namespace StreamDelay
{
	internal class ChatStyle
	{
		// "Sistema" es la letra de Windows (Segoe UI); las demás vienen de Google Fonts.
		public string Font = "Sistema";

		// Tamaño del texto en píxeles.
		public int Size = 15;

		public string Text = "#F4F4F5";

		// Nombres: "user" el color de cada persona, "fixed" todos de NameColor.
		public string NameMode = "user";

		public string NameColor = "#C09AFD";

		// Color de la raya de los mensajes con fondo y de los avisos.
		public string Accent = "#C09AFD";

		// "none" sin fondo, "bubbles" detrás de cada mensaje, "panel" detrás de todo el chat.
		public string Bg = "none";

		public string BgColor = "#111116";

		// Opacidad del fondo, de 0 a 100.
		public int BgOpacity = 85;

		// Esquinas del fondo: "round", "pill", "rect".
		public string Corners = "round";

		// Para que se lea sobre el juego: "shadow" sombra, "outline" contorno, "none" nada.
		public string Effect = "shadow";

		public bool Icons = true;

		public bool Badges = true;

		// Avisos de subs, regalos, raids, donaciones…
		public bool Events = true;

		// No enseñar los mensajes que empiezan por "!" (comandos).
		public bool HideCommands;

		// Segundos que se ve cada mensaje en pantalla (0 = siempre).
		public int Fade = 30;

		// Entrada de cada mensaje: "slide", "fade", "pop", "none".
		public string Anim = "slide";

		// "left" o "right".
		public string Align = "left";

		// Usar también este estilo en el panel del chat (no solo en pantalla).
		public bool Panel;

		public static readonly string[] Fonts = new string[16]
		{
			"Sistema", "Plus Jakarta Sans", "Inter", "Outfit", "Montserrat", "Poppins", "Rubik", "Nunito",
			"Oswald", "Bebas Neue", "Orbitron", "Bangers", "Comic Neue", "Permanent Marker", "Press Start 2P", "Roboto Mono"
		};

		public static readonly int[] Sizes = new int[8] { 13, 15, 18, 20, 24, 28, 32, 40 };

		public static readonly string[] PresetNames = new string[8] { "Clásico", "Burbujas", "Panel", "Grande", "Claro", "Neón", "Minimal", "Retro" };

		public static ChatStyle Preset(int i)
		{
			switch (i)
			{
			case 1:
				return new ChatStyle { Font = "Plus Jakarta Sans", Size = 16, Bg = "bubbles", BgColor = "#111116", BgOpacity = 85, Accent = "#C09AFD", Effect = "none" };
			case 2:
				return new ChatStyle { Font = "Inter", Size = 15, Bg = "panel", BgColor = "#0B0B10", BgOpacity = 70, Accent = "#C09AFD", Effect = "none", Corners = "round" };
			case 3:
				return new ChatStyle { Font = "Rubik", Size = 24, Bg = "none", Effect = "outline" };
			case 4:
				return new ChatStyle { Font = "Montserrat", Size = 16, Text = "#18181B", Bg = "bubbles", BgColor = "#FFFFFF", BgOpacity = 92, Accent = "#7C3AED", Effect = "none" };
			case 5:
				return new ChatStyle { Font = "Orbitron", Size = 15, Text = "#E0FFFF", NameMode = "fixed", NameColor = "#00F0FF", Accent = "#FF00E5", Bg = "bubbles", BgColor = "#05050A", BgOpacity = 80, Corners = "pill", Effect = "none" };
			case 6:
				return new ChatStyle { Font = "Inter", Size = 16, Icons = false, Badges = false, Bg = "none", Effect = "shadow", Anim = "fade" };
			case 7:
				return new ChatStyle { Font = "Press Start 2P", Size = 13, Text = "#39FF14", NameMode = "fixed", NameColor = "#FFFFFF", Accent = "#39FF14", Bg = "bubbles", BgColor = "#000000", BgOpacity = 85, Corners = "rect", Effect = "none", Anim = "none" };
			default:
				return new ChatStyle();
			}
		}

		// Lo que cambia un estilo listo (se mantienen qué se enseña, la duración y si va también en el panel).
		public void ApplyPreset(ChatStyle p)
		{
			Font = p.Font;
			Size = p.Size;
			Text = p.Text;
			NameMode = p.NameMode;
			NameColor = p.NameColor;
			Accent = p.Accent;
			Bg = p.Bg;
			BgColor = p.BgColor;
			BgOpacity = p.BgOpacity;
			Corners = p.Corners;
			Effect = p.Effect;
			Icons = p.Icons;
			Badges = p.Badges;
			Anim = p.Anim;
		}

		public ChatStyle Clone()
		{
			return (ChatStyle)MemberwiseClone();
		}

		public string ToJson()
		{
			Dictionary<string, object> d = new Dictionary<string, object>();
			d["font"] = Font;
			d["size"] = Size;
			d["tx"] = Text;
			d["names"] = NameMode;
			d["name"] = NameColor;
			d["acc"] = Accent;
			d["bg"] = Bg;
			d["bgc"] = BgColor;
			d["op"] = BgOpacity;
			d["corners"] = Corners;
			d["fx"] = Effect;
			d["icons"] = Icons;
			d["badges"] = Badges;
			d["events"] = Events;
			d["nocmd"] = HideCommands;
			d["fade"] = Fade;
			d["anim"] = Anim;
			d["align"] = Align;
			d["panel"] = Panel;
			return Web.ToJson(d);
		}

		public string Save()
		{
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(ToJson()));
		}

		public static ChatStyle Load(string saved)
		{
			ChatStyle s = new ChatStyle();
			try
			{
				object o = Web.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(saved)));
				string font = J.S(o, "font");
				if (Array.IndexOf(Fonts, font) >= 0)
				{
					s.Font = font;
				}
				s.Size = (int)Math.Max(10L, Math.Min(48L, J.N(o, s.Size, "size")));
				s.Text = Hex(J.S(o, "tx"), s.Text);
				s.NameMode = (J.S(o, "names") == "fixed") ? "fixed" : "user";
				s.NameColor = Hex(J.S(o, "name"), s.NameColor);
				s.Accent = Hex(J.S(o, "acc"), s.Accent);
				s.Bg = OneOf(J.S(o, "bg"), s.Bg, "none", "bubbles", "panel");
				s.BgColor = Hex(J.S(o, "bgc"), s.BgColor);
				s.BgOpacity = (int)Math.Max(0L, Math.Min(100L, J.N(o, s.BgOpacity, "op")));
				s.Corners = OneOf(J.S(o, "corners"), s.Corners, "round", "pill", "rect");
				s.Effect = OneOf(J.S(o, "fx"), s.Effect, "shadow", "outline", "none");
				s.Icons = J.S(o, "icons") != "False";
				s.Badges = J.S(o, "badges") != "False";
				s.Events = J.S(o, "events") != "False";
				s.HideCommands = J.S(o, "nocmd") == "True";
				s.Fade = (int)Math.Max(0L, Math.Min(600L, J.N(o, s.Fade, "fade")));
				s.Anim = OneOf(J.S(o, "anim"), s.Anim, "slide", "fade", "pop", "none");
				s.Align = OneOf(J.S(o, "align"), s.Align, "left", "right");
				s.Panel = J.S(o, "panel") == "True";
			}
			catch
			{
			}
			return s;
		}

		private static string OneOf(string v, string def, params string[] ok)
		{
			return (Array.IndexOf(ok, v) >= 0) ? v : def;
		}

		private static string Hex(string v, string def)
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
	}
}
