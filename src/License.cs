// Licencias: qué plan tiene esta copia de Vortexs Live.
//
//   Prueba    3 días con todo, desde la primera vez que se abre (no hace falta nada).
//   Básico    5 €/mes: delay dinámico, multistream y título (pestañas Directo, Ajustes, Título y
//             Registro), con los avisos del delay en el chat.
//   Completo  8 €/mes: todo (además clips, Chat, Overlays y Mando).
//
// Hay dos tipos de clave, y las dos se pegan en el mismo sitio (botón del plan, arriba):
//
//   * Claves de suscripción (Polar, polar.sh): las da la tienda al pagar. Se comprueban por internet
//     al abrir y cada 6 horas; si se cancela la suscripción, Polar revoca la clave y deja de valer.
//     Sin internet se sigue usando lo último comprobado durante 7 días.
//   * Claves firmadas "SD1-…": las crea Vortexs con crear-clave.ps1 y su clave privada (no está en
//     el código). Sirven para regalar: pueden ser del plan Básico o Completo, para siempre o hasta
//     una fecha, y llevan un nombre. Se comprueban sin internet con la clave pública de abajo.
//     Si una se filtra, se añade su número a Revoked y deja de valer en la siguiente versión.
//
// Esta parte no usa la ventana (se prueba en pruebas/VerifyChat.cs). La ventana y lo que se
// guarda están en LicenseUi.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace StreamDelay
{
	internal enum Plan
	{
		None = 0,
		Basic = 1,
		Full = 2
	}

	internal class LicenseInfo
	{
		public Plan Plan;

		// "prueba", "clave" (SD1-…) o "polar".
		public string Source = "";

		// Hasta cuándo vale (UTC). DateTime.MaxValue = para siempre o mientras dure la suscripción.
		public DateTime Expires = DateTime.MaxValue;

		// A nombre de quién está (claves SD1-) o el correo del cliente (Polar).
		public string Name = "";

		public uint Serial;

		// Por qué no vale (vacío si vale).
		public string Error = "";

		// Polar: hay que activar la clave en este PC (la licencia tiene límite de equipos).
		public bool NeedsActivation;

		// Polar: licencia de regalo (código de un solo uso), del plan Completo y para siempre.
		public bool Gift;

		public static LicenseInfo Bad(string why)
		{
			return new LicenseInfo { Error = why };
		}
	}

	internal static class License
	{
		public const int TrialDays = 3;

		public const string PriceBasic = "5 €/mes";

		public const string PriceFull = "8 €/mes";

		// ---------- Tienda (Polar) ----------
		// Tienda Vortexs_MC en polar.sh: id de la organización (Settings) y de la ventaja "License key"
		// de cada producto (Products → Benefits). Si PolarOrg estuviera vacío, solo valdrían las claves SD1-.
		// Las claves de Polar empiezan por VLB- (Básico) o VLC- (Completo).
		public const string PolarOrg = "f3c396f8-63db-4249-8b17-ca08b303c282";

		public const string PolarBenefitBasic = "fbce853e-32ae-4d4e-acce-558838710977";

		public const string PolarBenefitFull = "3346c6b5-e14c-4fd1-8412-7b47ff344101";

		// Regalos: producto privado "Vortexs Live Completo · Regalo" que se canjea gratis con un código
		// de descuento de un solo uso. Da el plan Completo para siempre; claves VLR-, 1 PC.
		public const string PolarBenefitGift = "b96352a3-f89d-43a9-b129-3708a34e538a";

		public const string PolarApi = "https://api.polar.sh";

		// Enlaces de compra (Checkout links de Polar). Vacíos: se abre la web con los precios.
		public const string BuyBasicUrl = "https://buy.polar.sh/polar_cl_krvpGoOXQ86zPp5NHnp0VO8iqdqbnA3izdywx1c6ziD";

		public const string BuyFullUrl = "https://buy.polar.sh/polar_cl_qSBK218sKDqXJaIQDBLZp9vfAGzlQe129N2eY0xIcfC";

		public const string PricesUrl = "https://manuelferjo.github.io/VortexsLive/#precios";

		// Gestionar o cancelar la suscripción (portal de clientes de Polar).
		public const string ManageUrl = "https://polar.sh/vortexsmc/portal";

		// ---------- Claves SD1- ----------

		private const string Prefix = "SD1-";

		// Lo que se firma va precedido de esto (así una firma de otra cosa no sirve como licencia).
		private const string Domain = "StreamDelay licencia 1\n";

		// Clave pública RSA de 1024 bits (exponente 65537).
		private const string Modulus = "df65358edd3f54ca6da1d399782b3fc74d06f85d5e7529393d11480583996311e0afe23259f57b3d0d9dc2a142b2bb02237758da64e329e363e4a960b0305fbff40991882b20afc70e7418d397cf6a844bd59cf38c6a02f8860ab607fb1b60dcc6c6567f84a7d3c37ed1194bebf78b849a827f4d6508f83636eec005be448235";

		// Números de claves SD1- que ya no valen (filtradas o devueltas).
		private static readonly uint[] Revoked = new uint[]
		{
			// Las 10 primeras claves de regalo (18/11/2026): valían en cualquier PC. Los regalos se
			// hacen ahora con códigos de Polar de un solo uso (ver licencias/LEEME.txt).
			4090557839u, 4141078507u, 3514884666u, 2616762506u, 523401616u,
			2728505509u, 470992271u, 598531470u, 3499124289u, 3403950286u
		};

		// Las fechas de caducidad van como días desde aquí.
		private static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static readonly byte[] Sha256Info = new byte[19]
		{
			0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20
		};

		public static bool IsSigned(string key)
		{
			return key != null && key.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
		}

		public static string PlanName(Plan p)
		{
			switch (p)
			{
			case Plan.Full:
				return "Completo";
			case Plan.Basic:
				return "Básico";
			default:
				return "Sin plan";
			}
		}

		// Clave SD1-: formato (antes de firmar), en Base64 URL sin "=":
		//   [1] versión (1)  [1] plan (1 Básico, 2 Completo)  [2] caduca: días desde 2026-01-01 (0 = nunca)
		//   [4] número de la clave  [0–40] nombre en UTF-8  + [128] firma RSA PKCS#1 v1.5 SHA-256
		public static LicenseInfo CheckSigned(string key, DateTime nowUtc)
		{
			if (!IsSigned(key))
			{
				return LicenseInfo.Bad("no es una clave de Vortexs Live");
			}
			byte[] all = FromB64(StripSpaces(key.Trim().Substring(Prefix.Length)));
			if (all == null || all.Length < 8 + 128 || all.Length > 8 + 40 + 128)
			{
				return LicenseInfo.Bad("la clave está incompleta o mal copiada");
			}
			int payloadLen = all.Length - 128;
			byte[] payload = new byte[payloadLen];
			byte[] sig = new byte[128];
			Buffer.BlockCopy(all, 0, payload, 0, payloadLen);
			Buffer.BlockCopy(all, payloadLen, sig, 0, 128);
			if (!Verify(Message(payload), sig))
			{
				return LicenseInfo.Bad("la clave no es válida (¿está bien copiada?)");
			}
			if (payload[0] != 1 || (payload[1] != 1 && payload[1] != 2))
			{
				return LicenseInfo.Bad("esta clave es de una versión más nueva de Vortexs Live");
			}
			int days = (payload[2] << 8) | payload[3];
			uint serial = (uint)((payload[4] << 24) | (payload[5] << 16) | (payload[6] << 8) | payload[7]);
			LicenseInfo info = new LicenseInfo
			{
				Plan = (Plan)payload[1],
				Source = "clave",
				Serial = serial,
				Name = Encoding.UTF8.GetString(payload, 8, payloadLen - 8),
				Expires = (days == 0) ? DateTime.MaxValue : Epoch.AddDays(days)
			};
			if (Array.IndexOf(Revoked, serial) >= 0)
			{
				info.Plan = Plan.None;
				info.Error = "esta clave se ha anulado";
			}
			else if (nowUtc >= info.Expires)
			{
				info.Plan = Plan.None;
				info.Error = "la clave caducó el " + info.Expires.ToLocalTime().ToString("d/M/yyyy", CultureInfo.InvariantCulture);
			}
			return info;
		}

		// Lo que se firma: el prefijo de dominio y los datos.
		internal static byte[] Message(byte[] payload)
		{
			byte[] d = Encoding.ASCII.GetBytes(Domain);
			byte[] m = new byte[d.Length + payload.Length];
			Buffer.BlockCopy(d, 0, m, 0, d.Length);
			Buffer.BlockCopy(payload, 0, m, d.Length, payload.Length);
			return m;
		}

		// RSA PKCS#1 v1.5 con SHA-256, hecho a mano con BigInteger (así funciona igual en cualquier
		// Windows con .NET 4, sin depender del proveedor criptográfico instalado).
		internal static bool Verify(byte[] message, byte[] sig)
		{
			BigInteger n = FromBigEndian(Hex(Modulus));
			BigInteger s = FromBigEndian(sig);
			if (s.Sign <= 0 || s >= n)
			{
				return false;
			}
			byte[] em = ToBigEndian(BigInteger.ModPow(s, 65537, n), 128);
			byte[] hash;
			using (SHA256 sha = new SHA256Managed())
			{
				hash = sha.ComputeHash(message);
			}
			// 00 01 FF…FF 00 DigestInfo hash
			int t = Sha256Info.Length + hash.Length;
			if (em[0] != 0 || em[1] != 1 || em[128 - t - 1] != 0)
			{
				return false;
			}
			for (int i = 2; i < 128 - t - 1; i++)
			{
				if (em[i] != 0xFF)
				{
					return false;
				}
			}
			bool ok = true;
			for (int i = 0; i < Sha256Info.Length; i++)
			{
				ok &= em[128 - t + i] == Sha256Info[i];
			}
			for (int i = 0; i < hash.Length; i++)
			{
				ok &= em[128 - hash.Length + i] == hash[i];
			}
			return ok;
		}

		// ---------- Polar ----------

		public static bool PolarReady
		{
			get
			{
				return PolarOrg.Length > 0;
			}
		}

		public static Plan PlanForBenefit(string benefitId)
		{
			if (string.IsNullOrEmpty(benefitId))
			{
				return Plan.None;
			}
			if (benefitId == PolarBenefitFull || benefitId == PolarBenefitGift)
			{
				return Plan.Full;
			}
			if (benefitId == PolarBenefitBasic)
			{
				return Plan.Basic;
			}
			return Plan.None;
		}

		// Respuesta de /v1/customer-portal/license-keys/validate (200).
		public static LicenseInfo FromPolar(Dictionary<string, object> r, DateTime nowUtc)
		{
			if (r == null)
			{
				return LicenseInfo.Bad("respuesta vacía de la tienda");
			}
			LicenseInfo info = new LicenseInfo
			{
				Source = "polar",
				Plan = PlanForBenefit(Http.Str(r, "benefit_id")),
				Gift = Http.Str(r, "benefit_id") == PolarBenefitGift
			};
			Dictionary<string, object> customer = Http.Obj(r, "customer");
			if (customer != null)
			{
				info.Name = Http.Str(customer, "email");
			}
			if (info.Plan == Plan.None)
			{
				info.Error = "esta clave no es de Vortexs Live";
				return info;
			}
			if (Http.Str(r, "status") != "granted")
			{
				info.Plan = Plan.None;
				info.Error = "la suscripción ya no está activa";
				return info;
			}
			string exp = Http.Str(r, "expires_at");
			DateTime e;
			if (exp.Length > 0 && DateTime.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out e))
			{
				info.Expires = e;
				if (nowUtc >= e)
				{
					info.Plan = Plan.None;
					info.Error = "la licencia ha caducado";
					return info;
				}
			}
			// Con límite de equipos hay que activarla en este PC (si no se ha hecho ya).
			info.NeedsActivation = r.ContainsKey("limit_activations") && r["limit_activations"] != null && Http.Obj(r, "activation") == null;
			return info;
		}

		// ---------- Prueba ----------

		public static LicenseInfo Trial(DateTime firstRunUtc, DateTime nowUtc)
		{
			DateTime end = firstRunUtc.AddDays(TrialDays);
			LicenseInfo info = new LicenseInfo
			{
				Source = "prueba",
				Expires = end,
				Plan = (nowUtc < end && nowUtc >= firstRunUtc.AddHours(-1)) ? Plan.Full : Plan.None
			};
			if (info.Plan == Plan.None)
			{
				info.Error = "la prueba de " + TrialDays + " días ha terminado";
			}
			return info;
		}

		// "2 días", "5 h", "menos de 1 h".
		public static string Left(DateTime until, DateTime nowUtc)
		{
			TimeSpan t = until - nowUtc;
			if (t.TotalHours >= 24)
			{
				int d = (int)Math.Ceiling(t.TotalDays - 0.0001);
				return d + (d == 1 ? " día" : " días");
			}
			if (t.TotalHours >= 1)
			{
				return (int)t.TotalHours + " h";
			}
			return "menos de 1 h";
		}

		// ---------- Utilidades ----------

		private static string StripSpaces(string s)
		{
			StringBuilder sb = new StringBuilder(s.Length);
			foreach (char c in s)
			{
				if (!char.IsWhiteSpace(c))
				{
					sb.Append(c);
				}
			}
			return sb.ToString();
		}

		internal static byte[] FromB64(string s)
		{
			try
			{
				s = s.Replace('-', '+').Replace('_', '/');
				while (s.Length % 4 != 0)
				{
					s += "=";
				}
				return Convert.FromBase64String(s);
			}
			catch
			{
				return null;
			}
		}

		private static byte[] Hex(string h)
		{
			byte[] b = new byte[h.Length / 2];
			for (int i = 0; i < b.Length; i++)
			{
				b[i] = byte.Parse(h.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			}
			return b;
		}

		private static BigInteger FromBigEndian(byte[] be)
		{
			byte[] le = new byte[be.Length + 1];
			for (int i = 0; i < be.Length; i++)
			{
				le[i] = be[be.Length - 1 - i];
			}
			return new BigInteger(le);
		}

		private static byte[] ToBigEndian(BigInteger v, int len)
		{
			byte[] le = v.ToByteArray();
			byte[] be = new byte[len];
			for (int i = 0; i < le.Length && i < len; i++)
			{
				be[len - 1 - i] = le[i];
			}
			return be;
		}
	}
}
