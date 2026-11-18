using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	// Comandos del chat (!delay, !web…) y moderación desde la página del chat conjunto.
	internal partial class MainForm
	{
		private bool cmdOn = true;

		private string cmdText = "!delay = ⏱️ {delay}\r\n!web = 🌐 www.vortexs.es";

		private const int CmdCooldownSec = 20;

		private readonly Dictionary<string, DateTime> cmdLast = new Dictionary<string, DateTime>();

		private DateTime chatStartedAt = DateTime.MinValue;

		private Dictionary<string, string> ParseCommands()
		{
			Dictionary<string, string> d = new Dictionary<string, string>();
			foreach (string line in cmdText.Replace("\r", "").Split('\n'))
			{
				int eq = line.IndexOf('=');
				if (eq <= 0)
				{
					continue;
				}
				string name = line.Substring(0, eq).Trim().ToLowerInvariant();
				string reply = line.Substring(eq + 1).Trim();
				if (name.Length < 2 || reply.Length == 0)
				{
					continue;
				}
				if (!name.StartsWith("!"))
				{
					name = "!" + name;
				}
				d[name] = reply;
			}
			return d;
		}

		private static string PlainText(ChatItem it)
		{
			StringBuilder sb = new StringBuilder();
			if (it.parts != null)
			{
				foreach (ChatPart p in it.parts)
				{
					sb.Append(p.e != null ? p.n : p.x);
				}
			}
			return sb.ToString().Trim();
		}

		private string DelayPhrase()
		{
			if (engine.Enabled && engine.TargetMs > 0)
			{
				return I18n.Tr("El directo va con ") + engine.TargetMs / 1000 + I18n.Tr(" s de delay.");
			}
			return I18n.Tr("Ahora mismo no hay delay: el directo va en tiempo real.");
		}

		// Cada mensaje del chat pasa por aquí (hilo del chat).
		private void OnChatItem(ChatItem it)
		{
			if (!cmdOn || it == null || it.t != "msg" || it.p == null)
			{
				return;
			}
			// Al conectar, Kick y YouTube mandan los últimos mensajes: no se contestan.
			if (DateTime.UtcNow - chatStartedAt < TimeSpan.FromSeconds(8.0))
			{
				return;
			}
			string text = PlainText(it);
			if (!text.StartsWith("!"))
			{
				return;
			}
			int sp = text.IndexOf(' ');
			string cmd = ((sp < 0) ? text : text.Substring(0, sp)).ToLowerInvariant();
			if (cmd == ClipCommand)
			{
				// Comando fijo: guarda los últimos 60 s en tu PC, solo si lo escribe el dueño del canal.
				ChatClip(it, (it.p == "twitch") ? "Twitch" : ((it.p == "kick") ? "Kick" : ((it.p == "tiktok") ? "TikTok" : "YouTube")));
				return;
			}
			// TikTok no deja escribir en el chat desde otros programas: no se puede contestar.
			if (it.p == "tiktok")
			{
				return;
			}
			string reply;
			if (!ParseCommands().TryGetValue(cmd, out reply))
			{
				return;
			}
			string key = it.p + cmd;
			lock (cmdLast)
			{
				DateTime last;
				if (cmdLast.TryGetValue(key, out last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(CmdCooldownSec))
				{
					return;
				}
				cmdLast[key] = DateTime.UtcNow;
			}
			string plat = (it.p == "twitch") ? "Twitch" : ((it.p == "kick") ? "Kick" : "YouTube");
			string msg = reply.Replace("{delay}", DelayPhrase()).Replace("{s}", (engine.TargetMs / 1000).ToString()).Replace("{user}", it.user ?? "").Replace("{plataforma}", plat);
			if (msg.StartsWith("!"))
			{
				msg = "→ " + msg;
			}
			string target = it.p;
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					Dictionary<string, object> res = ChatSendAll(msg, new string[1] { target });
					object v;
					if (res.TryGetValue(target, out v) && Convert.ToString(v) != "ok")
					{
						LogLater("Comando " + cmd + " en " + plat + ": no se pudo contestar (" + v + ").");
					}
				}
				catch (Exception ex)
				{
					LogLater("Comando " + cmd + ": " + ex.Message);
				}
			});
		}

		// ---------- Moderación ----------

		private Dictionary<string, object> ModAction(Dictionary<string, object> d)
		{
			Dictionary<string, object> res = new Dictionary<string, object>();
			string p = J.S(d, "p");
			string a = J.S(d, "a");
			string id = J.S(d, "id");
			string uid = J.S(d, "uid");
			string user = J.S(d, "user");
			string text = J.S(d, "text");
			string twCh = "";
			string kcCh = "";
			try
			{
				Invoke((Action)delegate
				{
					twCh = ChatHub.Clean(fldChatTw.Box.Text);
					kcCh = ChatHub.Clean(fldChatKick.Box.Text);
				});
			}
			catch
			{
			}
			int secs = (a == "t60") ? 60 : ((a == "t600") ? 600 : 0);
			bool del = a == "del";
			if (!del && a != "ban" && secs == 0)
			{
				res["error"] = "acción desconocida";
				return res;
			}
			Platform plat = (p == "twitch") ? twitch : ((p == "kick") ? ((Platform)kick) : ((p == "youtube") ? ((Platform)youtube) : null));
			if (plat == null)
			{
				res["error"] = "plataforma desconocida";
				return res;
			}
			if (!plat.Connected)
			{
				res["error"] = "conecta tu cuenta de " + plat.Name + " en la pestaña Título para moderar";
				return res;
			}
			if ((del && id.Length == 0 && p != "youtube") || (!del && uid.Length == 0))
			{
				res["error"] = "este mensaje no trae los datos necesarios";
				return res;
			}
			try
			{
				if (p == "twitch")
				{
					if (del)
					{
						twitch.DeleteMessage(twCh, id);
					}
					else
					{
						twitch.Ban(twCh, uid, secs, "StreamDelay");
					}
				}
				else if (p == "kick")
				{
					if (del)
					{
						kick.DeleteMessage(id);
					}
					else
					{
						kick.Ban(kcCh, uid, secs, "StreamDelay");
					}
				}
				else if (del)
				{
					youtube.DeleteMessage(uid, text);
				}
				else
				{
					youtube.Ban(uid, secs);
				}
				string what = del ? ("mensaje de " + user + " borrado") : ((secs > 0) ? (user + " expulsado " + secs / 60 + " min") : (user + " baneado"));
				LogLater("Moderación " + plat.Name + ": " + what + ".");
				res["ok"] = true;
				res["msg"] = what;
				if (del && chatServer != null)
				{
					// Se quita ya de la página (la plataforma también lo avisará).
					chatServer.Send(new ChatItem { t = "del", p = p, id = id });
				}
			}
			catch (Exception ex)
			{
				res["error"] = ex.Message;
				LogLater("Moderación " + plat.Name + ": " + ex.Message);
			}
			return res;
		}

		// ---------- Configuración ----------

		private void LoadCmdConfig(Dictionary<string, string> raw)
		{
			string v;
			if (raw.TryGetValue("cmd_on", out v))
			{
				cmdOn = v != "0";
			}
			if (raw.TryGetValue("cmd_text", out v) && v.Length > 0)
			{
				try
				{
					cmdText = Encoding.UTF8.GetString(Convert.FromBase64String(v));
				}
				catch
				{
				}
			}
		}

		private void SaveCmdConfig(List<string> lines)
		{
			lines.Add("cmd_on=" + (cmdOn ? "1" : "0"));
			lines.Add("cmd_text=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(cmdText)));
		}
	}
}
