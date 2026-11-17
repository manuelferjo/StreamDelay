using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StreamDelay
{
	internal class FfmpegConn : IConn
	{
		private readonly Process p;

		private readonly Stream s;

		private readonly byte[] hdr = new byte[11];

		private readonly byte[] end = new byte[4];

		public FfmpegConn(string ffmpeg, string url, Action<string> log, Func<string, string> mask)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo(ffmpeg, "-hide_banner -loglevel error -analyzeduration 2000000 -f flv -i pipe:0 -c copy -f flv -flvflags no_duration_filesize \"" + url + "\"")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardError = true
			};
			try
			{
				if (Console.InputEncoding.GetPreamble().Length > 0)
				{
					Console.InputEncoding = new UTF8Encoding(false);
				}
			}
			catch
			{
			}
			p = Process.Start(startInfo);
			try
			{
				p.PriorityClass = ProcessPriorityClass.AboveNormal;
			}
			catch
			{
			}
			p.ErrorDataReceived += delegate(object o, DataReceivedEventArgs e)
			{
				if (!string.IsNullOrEmpty(e.Data))
				{
					string text = Explain(e.Data);
					if (text != null)
					{
						log("ffmpeg: " + mask(text));
					}
				}
			};
			p.BeginErrorReadLine();
			s = p.StandardInput.BaseStream;
			s.Write(new byte[13]
			{
				70, 76, 86, 1, 5, 0, 0, 0, 9, 0,
				0, 0, 0
			}, 0, 13);
		}

		public void Write(byte type, long ts, byte[] data)
		{
			if (p.HasExited)
			{
				throw new IOException("ffmpeg terminó");
			}
			int num = data.Length;
			hdr[0] = type;
			hdr[1] = (byte)(num >> 16);
			hdr[2] = (byte)(num >> 8);
			hdr[3] = (byte)num;
			hdr[4] = (byte)(ts >> 16);
			hdr[5] = (byte)(ts >> 8);
			hdr[6] = (byte)ts;
			hdr[7] = (byte)(ts >> 24);
			hdr[8] = (hdr[9] = (hdr[10] = 0));
			int num2 = num + 11;
			end[0] = (byte)(num2 >> 24);
			end[1] = (byte)(num2 >> 16);
			end[2] = (byte)(num2 >> 8);
			end[3] = (byte)num2;
			s.Write(hdr, 0, 11);
			s.Write(data, 0, num);
			s.Write(end, 0, 4);
		}

		public void Close(bool polite)
		{
			try
			{
				s.Close();
			}
			catch
			{
			}
			try
			{
				if (!p.WaitForExit(3000))
				{
					p.Kill();
				}
			}
			catch
			{
			}
		}

		private static string Explain(string line)
		{
			string text = line.ToLowerInvariant();
			if (text.Contains("failed to resolve hostname") || text.Contains("no such host"))
			{
				return line + "  → No se encuentra el servidor: revisa la URL o tu conexión a internet.";
			}
			if (text.Contains("connection refused") || text.Contains("cannot open connection"))
			{
				return line + "  → El servidor de la plataforma rechazó la conexión: revisa la URL del servidor.";
			}
			if (text.Contains("server error") || text.Contains("unexpected stream") || text.Contains("netstream.publish.badname"))
			{
				return line + "  → La plataforma rechazó la clave: cópiala de nuevo (sin espacios).";
			}
			if (text.Contains("tls") || text.Contains("ssl") || text.Contains("certificate"))
			{
				return line + "  → Fallo de conexión segura (rtmps): prueba la versión rtmp:// del servidor si la plataforma la ofrece.";
			}
			if (text.Contains("error muxing"))
			{
				return line + "  → Se cortó la conexión con la plataforma.";
			}
			if (text.Contains("10054") || text.Contains("10053") || text.Contains("-138") || text.Contains("broken pipe") || text.Contains("connection reset") || text.Contains("i/o error"))
			{
				return null;
			}
			if (text.StartsWith("    last message repeated"))
			{
				return null;
			}
			return line;
		}
	}
}
