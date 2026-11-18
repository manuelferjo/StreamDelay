// TikTok en vertical: OBS crea una imagen vertical (plugin Aitum Vertical) y la manda a Vortexs Live por
// el puerto siguiente al de siempre (1935 → 1936). Un segundo motor, igual que el principal, le pone el
// mismo delay y la envía solo a TikTok; los demás destinos siguen recibiendo la imagen horizontal.
// Arranca, se para, se corta y cambia de delay a la vez que el principal (atajos, Mando y chat incluidos).
// Se activa en Ajustes → Destinos → TikTok → Editar → "Imagen vertical".
using System;
using System.Collections.Generic;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// Motor de la imagen vertical (solo TikTok).
		private readonly Engine engineV = new Engine();

		// TikTok recibe la imagen vertical (VortexsLive.ini: tiktok_vertical=1).
		private bool tiktokVertical;

		private bool VerticalOn
		{
			get
			{
				return tiktokVertical && dTikTok.Enabled;
			}
		}

		private int VerticalPort
		{
			get
			{
				return port + 1;
			}
		}

		// Lo que hay que poner en Aitum Vertical como servidor.
		private string VerticalServer
		{
			get
			{
				return "rtmp://" + (tgLan.Checked ? LanIp() : "127.0.0.1") + ":" + VerticalPort + "/app";
			}
		}

		// Destinos de los dos motores (para la franja de Directo, el Mando y los avisos).
		private Sink[] AllSinks()
		{
			List<Sink> all = new List<Sink>();
			try
			{
				all.AddRange(engine.Sinks);
				if (engineV.Running)
				{
					all.AddRange(engineV.Sinks);
				}
			}
			catch
			{
			}
			return all.ToArray();
		}

		// El motor vertical lleva siempre el mismo delay que el principal.
		private void SyncVertical()
		{
			engineV.TargetMs = engine.TargetMs;
			engineV.Enabled = engine.Enabled;
			engineV.ReplayOnIncrease = engine.ReplayOnIncrease;
		}

		// Se llama al iniciar el servicio, con el principal ya en marcha. Lanza la excepción si no puede abrir el puerto.
		private void StartVertical()
		{
			engineV.Log = delegate(string msg)
			{
				lock (pendingLog)
				{
					pendingLog.Enqueue(DateTime.Now.ToString("HH:mm:ss ") + "TikTok vertical: " + msg);
				}
			};
			engineV.UseFfmpeg = engine.UseFfmpeg;
			engineV.FfmpegPath = engine.FfmpegPath;
			engineV.InputPort = VerticalPort;
			engineV.AllowLan = engine.AllowLan;
			engineV.Destinations = new List<Destination> { dTikTok };
			SyncVertical();
			engineV.Start();
			AppendLog("TikTok vertical: esperando a Aitum Vertical en " + VerticalServer + " (con el mismo delay).");
		}

		private void StopVertical()
		{
			if (engineV.Running)
			{
				engineV.Stop();
			}
		}

		private void CutVertical()
		{
			if (engineV.Running)
			{
				engineV.CutNow();
			}
		}
	}
}
