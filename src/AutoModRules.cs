// Reglas de la moderación automática (sin ventana): palabras prohibidas, spam y usuarios ocultos.
// La ventana y lo que se hace con cada mensaje están en AutoMod.cs. Se prueban en pruebas\VerifyChat.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StreamDelay
{
	// Las reglas (sin ventana), para poder probarlas en pruebas\VerifyChat.cs.
	internal class AutoModRules
	{
		// Bots de chat conocidos (Twitch, Kick y YouTube).
		public static readonly string[] KnownBots = new string[]
		{
			"nightbot", "streamelements", "streamlabs", "moobot", "fossabot", "wizebot", "sery_bot", "botrixoficial",
			"kickbot", "botisimo", "commanderroot", "soundalerts", "pokemoncommunitygame", "streamlootsbot", "deepbot",
			"phantombot", "coebot", "ankhbot", "vivbot", "own3d", "blerp", "kofistreambot", "tangiabot", "lumiastream"
		};

		public bool On;

		public string Words = "";

		// Qué hacer: 0 solo ocultar, 1 borrar, 2 expulsar 1 min, 3 expulsar 10 min, 4 banear.
		public int WordAct = 1;

		public bool Links = true;

		public bool Caps;

		public bool Repeat = true;

		public bool Emotes;

		public int SpamAct = 1;

		public bool ExemptSubs = true;

		public bool HideBots;

		public readonly HashSet<string> Ignored = new HashSet<string>();

		private Regex wordRx;

		private string wordRxFor;

		private readonly Dictionary<string, List<DateTime>> seen = new Dictionary<string, List<DateTime>>();

		private static readonly Regex LinkRx = new Regex("(https?://|www\\.)\\S+|\\b[a-z0-9-]{2,}\\.(com|net|org|es|tv|gg|io|ly|xyz|me|co|link|app|shop|live|info|ru|top|site|store|online|click|to)(/\\S*)?\\b", RegexOptions.IgnoreCase);

		// Minúsculas y sin tildes, para que "Tónto" y "tonto" sean lo mismo.
		public static string Fold(string s)
		{
			string d = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
			StringBuilder sb = new StringBuilder(d.Length);
			foreach (char c in d)
			{
				if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
				{
					sb.Append(c);
				}
			}
			return sb.ToString();
		}

		private Regex WordRegex()
		{
			string words = Words;
			if (wordRxFor == words)
			{
				return wordRx;
			}
			List<string> parts = new List<string>();
			foreach (string line in (words ?? "").Replace("\r", "").Split('\n'))
			{
				string w = Fold(line.Trim());
				if (w.Length == 0)
				{
					continue;
				}
				string rx = Regex.Escape(w).Replace("\\*", "[\\p{L}\\p{N}]*");
				// Palabra entera: "tonto" no salta con "tontorrón" (para eso, "tonto*").
				parts.Add("(?<![\\p{L}\\p{N}])" + rx + "(?![\\p{L}\\p{N}])");
			}
			wordRx = (parts.Count == 0) ? null : new Regex(string.Join("|", parts.ToArray()), RegexOptions.CultureInvariant);
			wordRxFor = words;
			return wordRx;
		}

		public bool IsHiddenUser(string user)
		{
			string u = (user ?? "").Trim().TrimStart('@').ToLowerInvariant();
			if (u.Length == 0)
			{
				return false;
			}
			lock (Ignored)
			{
				if (Ignored.Contains(u))
				{
					return true;
				}
			}
			return HideBots && Array.IndexOf(KnownBots, u) >= 0;
		}

		private static bool Has(string[] roles, params string[] any)
		{
			if (roles == null)
			{
				return false;
			}
			foreach (string r in any)
			{
				if (Array.IndexOf(roles, r) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		public static string Plain(ChatItem it)
		{
			StringBuilder sb = new StringBuilder();
			if (it.parts != null)
			{
				foreach (ChatPart p in it.parts)
				{
					sb.Append((p.e != null) ? p.n : p.x);
				}
			}
			return sb.ToString().Trim();
		}

		// null: el mensaje sale. Si no, el motivo; act = qué hacer (-1: usuario oculto, no se hace nada más).
		public string Check(ChatItem it, out int act)
		{
			act = -1;
			if (it == null || it.t != "msg")
			{
				return null;
			}
			if (IsHiddenUser(it.user))
			{
				return "usuario oculto";
			}
			if (!On || Has(it.roles, "broadcaster", "mod"))
			{
				return null;
			}
			if (ExemptSubs && Has(it.roles, "vip", "sub", "member", "founder"))
			{
				return null;
			}
			string text = Plain(it);
			Regex rx = WordRegex();
			if (rx != null && rx.IsMatch(Fold(text)))
			{
				act = WordAct;
				return "palabra prohibida";
			}
			string spam = SpamReason(it, text);
			if (spam != null)
			{
				act = SpamAct;
			}
			return spam;
		}

		private string SpamReason(ChatItem it, string text)
		{
			// Enlaces y mayúsculas solo en lo escrito (los nombres de los emotes, como KEKW, no cuentan).
			StringBuilder written = new StringBuilder();
			if (it.parts != null)
			{
				foreach (ChatPart p in it.parts)
				{
					if (p.e == null)
					{
						written.Append(p.x);
					}
				}
			}
			string own = written.ToString();
			if (Links && LinkRx.IsMatch(own))
			{
				return "enlace";
			}
			if (Caps)
			{
				int letters = 0;
				int upper = 0;
				foreach (char c in own)
				{
					if (char.IsLetter(c))
					{
						letters++;
						if (char.IsUpper(c))
						{
							upper++;
						}
					}
				}
				if (letters >= 12 && upper * 10 >= letters * 7)
				{
					return "mayúsculas";
				}
			}
			if (Emotes)
			{
				int emotes = 0;
				if (it.parts != null)
				{
					foreach (ChatPart p in it.parts)
					{
						if (p.e != null)
						{
							emotes++;
						}
					}
				}
				if (emotes >= 12)
				{
					return "demasiados emotes";
				}
			}
			if (Repeat && text.Length > 0)
			{
				// El mismo mensaje de la misma persona por tercera vez en un minuto.
				string key = it.p + "|" + (it.user ?? "").ToLowerInvariant() + "|" + Fold(text);
				DateTime now = DateTime.UtcNow;
				lock (seen)
				{
					if (seen.Count > 3000)
					{
						seen.Clear();
					}
					List<DateTime> l;
					if (!seen.TryGetValue(key, out l))
					{
						l = new List<DateTime>();
						seen[key] = l;
					}
					l.RemoveAll((DateTime t) => now - t > TimeSpan.FromSeconds(60.0));
					l.Add(now);
					if (l.Count >= 3)
					{
						return "mensaje repetido";
					}
				}
			}
			return null;
		}
	}
}
