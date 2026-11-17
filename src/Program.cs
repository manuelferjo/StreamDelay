using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StreamDelay
{
	internal static class Program
	{
		[DllImport("user32.dll")]
		private static extern bool SetProcessDPIAware();

		[STAThread]
		private static void Main()
		{
			try
			{
				if (Environment.OSVersion.Version.Major >= 6)
				{
					SetProcessDPIAware();
				}
			}
			catch
			{
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Theme.Init();
			Application.Run(new MainForm());
		}
	}
}
