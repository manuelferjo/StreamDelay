// Túnel de ngrok para el enlace de los mods: lleva https://<tu-dominio>.ngrok-free.app hasta la
// página local de StreamDelay. El dominio gratuito de ngrok es fijo para cada cuenta, así que los
// enlaces de los mods no cambian. ngrok.exe se descarga la primera vez en
// %LOCALAPPDATA%\StreamDelay\ngrok y se cierra con StreamDelay (aunque StreamDelay se cierre de golpe).
// Por el túnel solo responde el chat de los mods (/mod…), y solo con la clave de cada mod.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace StreamDelay
{
	internal class NgrokTunnel
	{
		private const string DownloadUrl = "https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-windows-amd64.zip";

		public Action<string> Log = delegate { };

		// Para la ventana: cambió el estado o el enlace.
		public Action Changed = delegate { };

		public volatile string State = "apagado";

		public volatile string Url = "";

		private Process proc;

		private volatile bool wanted;

		private int port;

		private string token = "";

		private readonly object lk = new object();

		private IntPtr job = IntPtr.Zero;

		public static string Folder
		{
			get
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDelay", "ngrok");
			}
		}

		public static string Exe
		{
			get
			{
				return Path.Combine(Folder, "ngrok.exe");
			}
		}

		public bool Running
		{
			get
			{
				return wanted;
			}
		}

		private void Set(string state, string url)
		{
			State = state;
			Url = url;
			try
			{
				Changed();
			}
			catch
			{
			}
		}

		// Arranca (o vuelve a arrancar con otro puerto o clave). Se hace en segundo plano.
		public void Start(int localPort, string authToken)
		{
			lock (lk)
			{
				if (wanted && proc != null && localPort == port && authToken == token)
				{
					return;
				}
				wanted = true;
				port = localPort;
				token = authToken;
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				Run();
			});
		}

		public void Stop()
		{
			lock (lk)
			{
				wanted = false;
				Kill();
			}
			Set("apagado", "");
		}

		private void Kill()
		{
			try
			{
				if (proc != null && !proc.HasExited)
				{
					proc.Kill();
				}
			}
			catch
			{
			}
			proc = null;
		}

		private void Run()
		{
			int myPort;
			string myToken;
			lock (lk)
			{
				Kill();
				myPort = port;
				myToken = token;
			}
			try
			{
				if (!File.Exists(Exe))
				{
					Set("descargando ngrok…", "");
					Download();
				}
				Set("conectando…", "");
				ProcessStartInfo psi = new ProcessStartInfo(Exe, "http 127.0.0.1:" + myPort + " --log stdout --log-format logfmt")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					WorkingDirectory = Folder
				};
				// La clave va por variable de entorno, no en la línea de órdenes.
				psi.EnvironmentVariables["NGROK_AUTHTOKEN"] = myToken;
				Process p = new Process { StartInfo = psi, EnableRaisingEvents = true };
				p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { OnLine(e.Data); };
				p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { OnLine(e.Data); };
				p.Exited += delegate
				{
					OnExit(p);
				};
				lock (lk)
				{
					if (!wanted)
					{
						return;
					}
					p.Start();
					proc = p;
					AddToJob(p);
				}
				p.BeginOutputReadLine();
				p.BeginErrorReadLine();
			}
			catch (Exception ex)
			{
				Set("error: " + ex.Message, "");
				Log("Enlace para mods: " + ex.Message);
			}
		}

		// ngrok escribe líneas tipo  t=… lvl=info msg="started tunnel" … url=https://xxx.ngrok-free.app
		private void OnLine(string line)
		{
			if (string.IsNullOrEmpty(line))
			{
				return;
			}
			int i = line.IndexOf("url=https://", StringComparison.Ordinal);
			if (i >= 0 && (line.Contains("started tunnel") || line.Contains(".ngrok")))
			{
				string url = line.Substring(i + 4);
				int sp = url.IndexOfAny(new char[3] { ' ', '"', '\t' });
				if (sp > 0)
				{
					url = url.Substring(0, sp);
				}
				Set("conectado", url.TrimEnd('/'));
				Log("Enlace para mods activo: " + Url);
				return;
			}
			if (line.Contains("lvl=eror") || line.Contains("lvl=crit") || line.Contains("ERR_NGROK"))
			{
				Set("error: " + Friendly(line), "");
				Log("Enlace para mods: " + Friendly(line));
			}
		}

		private static string Friendly(string line)
		{
			if (line.Contains("ERR_NGROK_4018") || line.Contains("ERR_NGROK_105") || line.ToLowerInvariant().Contains("authtoken"))
			{
				return "la clave de ngrok no es válida (cópiala otra vez de tu panel de ngrok)";
			}
			if (line.Contains("ERR_NGROK_108"))
			{
				return "tu cuenta de ngrok ya tiene un túnel abierto en otro sitio (ciérralo y vuelve a activar)";
			}
			int e = line.IndexOf("err=", StringComparison.Ordinal);
			string msg = (e >= 0) ? line.Substring(e + 4).Trim().Trim('"') : line;
			return (msg.Length > 160) ? msg.Substring(0, 160) : msg;
		}

		private void OnExit(Process p)
		{
			bool again;
			lock (lk)
			{
				if (proc != p)
				{
					return;
				}
				proc = null;
				again = wanted;
			}
			if (!again)
			{
				return;
			}
			// Se ha cerrado solo: se vuelve a abrir en 15 s (si la clave está mal, el error se queda a la vista).
			if (!State.StartsWith("error"))
			{
				Set("reconectando…", "");
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				Thread.Sleep(15000);
				if (wanted)
				{
					lock (lk)
					{
						if (proc != null)
						{
							return;
						}
					}
					Run();
				}
			});
		}

		private static void Download()
		{
			Directory.CreateDirectory(Folder);
			string zip = Path.Combine(Folder, "ngrok.zip");
			using (WebClient wc = new WebClient())
			{
				wc.Headers["User-Agent"] = "StreamDelay";
				wc.DownloadFile(DownloadUrl, zip);
			}
			using (ZipArchive z = ZipFile.OpenRead(zip))
			{
				foreach (ZipArchiveEntry e in z.Entries)
				{
					if (e.Name.Equals("ngrok.exe", StringComparison.OrdinalIgnoreCase))
					{
						string tmp = Exe + ".tmp";
						e.ExtractToFile(tmp, true);
						if (File.Exists(Exe))
						{
							File.Delete(Exe);
						}
						File.Move(tmp, Exe);
					}
				}
			}
			File.Delete(zip);
			if (!File.Exists(Exe))
			{
				throw new Exception("no se pudo descargar ngrok");
			}
		}

		// ---------- Que ngrok se cierre con StreamDelay (también si se cierra de golpe) ----------

		[StructLayout(LayoutKind.Sequential)]
		private struct BasicLimit
		{
			public long PerProcessUserTimeLimit;

			public long PerJobUserTimeLimit;

			public uint LimitFlags;

			public UIntPtr MinimumWorkingSetSize;

			public UIntPtr MaximumWorkingSetSize;

			public uint ActiveProcessLimit;

			public UIntPtr Affinity;

			public uint PriorityClass;

			public uint SchedulingClass;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct IoCounters
		{
			public ulong a;

			public ulong b;

			public ulong c;

			public ulong d;

			public ulong e;

			public ulong f;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct ExtendedLimit
		{
			public BasicLimit Basic;

			public IoCounters Io;

			public UIntPtr ProcessMemoryLimit;

			public UIntPtr JobMemoryLimit;

			public UIntPtr PeakProcessMemoryUsed;

			public UIntPtr PeakJobMemoryUsed;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr CreateJobObject(IntPtr a, string name);

		[DllImport("kernel32.dll")]
		private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimit info, int size);

		[DllImport("kernel32.dll")]
		private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

		private void AddToJob(Process p)
		{
			try
			{
				if (job == IntPtr.Zero)
				{
					job = CreateJobObject(IntPtr.Zero, null);
					ExtendedLimit info = default(ExtendedLimit);
					info.Basic.LimitFlags = 0x2000u;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
					SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(ExtendedLimit)));
				}
				AssignProcessToJobObject(job, p.Handle);
			}
			catch
			{
			}
		}
	}
}
