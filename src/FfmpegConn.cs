// Modo clásico: en vez de conectar con la plataforma directamente, se abre ffmpeg y se le pasa
// el vídeo como un archivo FLV por su entrada; ffmpeg lo reenvía tal cual (-c copy) al servidor.
// Solo admite una calidad. Los errores de ffmpeg se traducen a algo que se entienda.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StreamDelay
{
	internal class FfmpegConn : IConn
	{
		private readonly Process p;

		// Entrada de ffmpeg.
		private readonly Stream s;

		// Cabecera de etiqueta FLV (11 bytes) y tamaño de la etiqueta anterior (4).
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
				// Que la entrada de ffmpeg no empiece con la marca BOM de UTF-8 (rompería el FLV).
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
					string explained = Explain(e.Data);
					if (explained != null)
					{
						log("ffmpeg: " + mask(explained));
					}
				}
			};
			p.BeginErrorReadLine();
			s = p.StandardInput.BaseStream;
			// Cabecera FLV: "FLV", versión 1, audio y vídeo, 9 bytes, y tamaño de etiqueta anterior 0.
			s.Write(new byte[13]
			{
				70, 76, 86, 1, 5, 0, 0, 0, 9, 0,
				0, 0, 0
			}, 0, 13);
		}

		// Escribe el paquete como una etiqueta FLV.
		public void Write(byte type, long ts, byte[] data)
		{
			if (p.HasExited)
			{
				throw new IOException("ffmpeg terminó");
			}
			int length = data.Length;
			hdr[0] = type;
			hdr[1] = (byte)(length >> 16);
			hdr[2] = (byte)(length >> 8);
			hdr[3] = (byte)length;
			// Marca de tiempo: 3 bytes y el cuarto (el más alto) al final.
			hdr[4] = (byte)(ts >> 16);
			hdr[5] = (byte)(ts >> 8);
			hdr[6] = (byte)ts;
			hdr[7] = (byte)(ts >> 24);
			hdr[8] = (hdr[9] = (hdr[10] = 0));
			int tagSize = length + 11;
			end[0] = (byte)(tagSize >> 24);
			end[1] = (byte)(tagSize >> 16);
			end[2] = (byte)(tagSize >> 8);
			end[3] = (byte)tagSize;
			s.Write(hdr, 0, 11);
			s.Write(data, 0, length);
			s.Write(end, 0, 4);
		}

		// Cierra la entrada (ffmpeg termina la emisión) y, si no acaba en 3 s, lo cierra a la fuerza.
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

		// Añade una explicación a los errores conocidos de ffmpeg. null = no enseñarlo
		// (cortes de conexión que ya se avisan de otra forma, y líneas repetidas).
		private static string Explain(string line)
		{
			string lower = line.ToLowerInvariant();
			if (lower.Contains("failed to resolve hostname") || lower.Contains("no such host"))
			{
				return line + "  → No se encuentra el servidor: revisa la URL o tu conexión a internet.";
			}
			if (lower.Contains("connection refused") || lower.Contains("cannot open connection"))
			{
				return line + "  → El servidor de la plataforma rechazó la conexión: revisa la URL del servidor.";
			}
			if (lower.Contains("server error") || lower.Contains("unexpected stream") || lower.Contains("netstream.publish.badname"))
			{
				return line + "  → La plataforma rechazó la clave: cópiala de nuevo (sin espacios).";
			}
			if (lower.Contains("tls") || lower.Contains("ssl") || lower.Contains("certificate"))
			{
				return line + "  → Fallo de conexión segura (rtmps): prueba la versión rtmp:// del servidor si la plataforma la ofrece.";
			}
			if (lower.Contains("error muxing"))
			{
				return line + "  → Se cortó la conexión con la plataforma.";
			}
			if (lower.Contains("10054") || lower.Contains("10053") || lower.Contains("-138") || lower.Contains("broken pipe") || lower.Contains("connection reset") || lower.Contains("i/o error"))
			{
				return null;
			}
			if (lower.StartsWith("    last message repeated"))
			{
				return null;
			}
			return line;
		}
	}
}
