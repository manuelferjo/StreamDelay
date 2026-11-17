// Clips: guarda los últimos 60 s que ha visto el público (con el delay ya aplicado) en
// Vídeos\StreamDelay. Se pide con !clip en el chat, Ctrl+Alt+C, el botón Clip de Directo, el
// Mando o el Stream Deck. Si hay ffmpeg en el PC, el clip se pasa a MP4 (sin volver a codificar);
// si no, se queda en FLV (se abre con VLC).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private const int ClipSeconds = 60;

		// Entre dos clips tiene que pasar al menos esto (si diez personas escriben !clip, se guarda uno).
		private const int ClipCooldownSec = 15;

		private DateTime lastClipAt = DateTime.MinValue;

		private volatile bool clipBusy;

		private static string ClipFolder
		{
			get
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "StreamDelay");
			}
		}

		// Guarda un clip en otro hilo. who: quién lo pidió (para el registro). done(ok, mensaje) se
		// llama desde ese hilo al terminar (o enseguida si no se puede); puede ser null.
		// Devuelve false si no se empieza (ya se está guardando o se acaba de guardar uno).
		private bool SaveClip(string who, Action<bool, string> done)
		{
			lock (this)
			{
				if (clipBusy || (DateTime.UtcNow - lastClipAt).TotalSeconds < ClipCooldownSec)
				{
					if (done != null)
					{
						done(false, "Se acaba de guardar un clip: espera unos segundos.");
					}
					return false;
				}
				clipBusy = true;
				lastClipAt = DateTime.UtcNow;
			}
			int mainTrack = engine.MainTrack;
			ThreadPool.QueueUserWorkItem(delegate
			{
				bool ok = false;
				string message;
				try
				{
					Directory.CreateDirectory(ClipFolder);
					string flv = Path.Combine(ClipFolder, "Clip " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".flv");
					double seconds = engine.Clips.Save(flv, ClipSeconds, mainTrack);
					string file = ToMp4(flv) ?? flv;
					ok = true;
					message = "Clip de " + Math.Round(seconds) + " s guardado: " + file;
				}
				catch (Exception ex)
				{
					message = "No se pudo guardar el clip: " + ex.Message + ".";
				}
				clipBusy = false;
				LogLater(message + " (" + who + ")");
				if (done != null)
				{
					done(ok, message);
				}
			});
			return true;
		}

		// Pasa el FLV a MP4 con ffmpeg (-c copy, tarda un segundo). null si no hay ffmpeg o falla
		// (entonces se queda el FLV).
		private static string ToMp4(string flv)
		{
			string mp4 = Path.ChangeExtension(flv, ".mp4");
			foreach (string ffmpeg in Engine.FfmpegCandidates())
			{
				try
				{
					ProcessStartInfo startInfo = new ProcessStartInfo(ffmpeg, "-hide_banner -loglevel error -y -i \"" + flv + "\" -c copy -movflags +faststart \"" + mp4 + "\"")
					{
						UseShellExecute = false,
						CreateNoWindow = true
					};
					using (Process process = Process.Start(startInfo))
					{
						if (!process.WaitForExit(60000))
						{
							try
							{
								process.Kill();
							}
							catch
							{
							}
							continue;
						}
						if (process.ExitCode == 0 && File.Exists(mp4) && new FileInfo(mp4).Length > 0)
						{
							File.Delete(flv);
							return mp4;
						}
					}
				}
				catch
				{
				}
			}
			try
			{
				if (File.Exists(mp4))
				{
					File.Delete(mp4);
				}
			}
			catch
			{
			}
			return null;
		}

		private void OpenClipFolder()
		{
			try
			{
				Directory.CreateDirectory(ClipFolder);
				Process.Start(new ProcessStartInfo(ClipFolder)
				{
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				AppendLog("No se pudo abrir la carpeta de clips (" + ex.Message + ").");
			}
		}

		// !clip en el chat: guarda el clip y contesta en esa plataforma.
		private void ChatClip(ChatItem it, string plat)
		{
			string target = it.p;
			string user = it.user ?? "";
			SaveClip("!clip de " + user + " en " + plat, delegate(bool ok, string message)
			{
				if (!ok)
				{
					return;
				}
				try
				{
					Dictionary<string, object> res = ChatSendAll(I18n.Tr("🎬 Clip guardado: los últimos ") + ClipSeconds + I18n.Tr(" s. ¡Gracias, ") + user + "!", new string[1] { target });
					object v;
					if (res.TryGetValue(target, out v) && Convert.ToString(v) != "ok")
					{
						LogLater("!clip en " + plat + ": no se pudo contestar (" + v + ").");
					}
				}
				catch (Exception ex)
				{
					LogLater("!clip: " + ex.Message);
				}
			});
		}
	}
}
