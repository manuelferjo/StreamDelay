using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace StreamDelay
{
	internal class RtmpServer
	{
		public Action<string> Log = delegate
		{
		};

		public Action<RtmpSession> PublishStart = delegate
		{
		};

		public Action<RtmpSession, byte, uint, byte[]> Packet = delegate
		{
		};

		public Action<RtmpSession> PublishEnd = delegate
		{
		};

		private readonly List<TcpListener> listeners = new List<TcpListener>();

		private readonly List<RtmpSession> sessions = new List<RtmpSession>();

		private volatile bool running;

		public void Start(int port, bool lan)
		{
			running = true;
			IPAddress[] array = (lan ? new IPAddress[2]
			{
				IPAddress.Any,
				IPAddress.IPv6Any
			} : new IPAddress[2]
			{
				IPAddress.Loopback,
				IPAddress.IPv6Loopback
			});
			Exception ex = null;
			IPAddress[] array2 = array;
			foreach (IPAddress iPAddress in array2)
			{
				try
				{
					TcpListener l = new TcpListener(iPAddress, port);
					if (iPAddress.AddressFamily == AddressFamily.InterNetworkV6)
					{
						l.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
					}
					l.Start();
					listeners.Add(l);
					Thread thread = new Thread((ThreadStart)delegate
					{
						AcceptLoop(l);
					});
					thread.IsBackground = true;
					thread.Name = "rtmp-accept";
					Thread thread2 = thread;
					thread2.Start();
				}
				catch (Exception ex2)
				{
					if (iPAddress.AddressFamily == AddressFamily.InterNetwork)
					{
						ex = ex2;
					}
				}
			}
			if (ex != null)
			{
				Stop();
				throw new IOException("El puerto " + port + " está ocupado o bloqueado (" + ex.Message + ")");
			}
		}

		public void Stop()
		{
			running = false;
			foreach (TcpListener listener in listeners)
			{
				try
				{
					listener.Stop();
				}
				catch
				{
				}
			}
			listeners.Clear();
			RtmpSession[] array;
			lock (sessions)
			{
				array = sessions.ToArray();
				sessions.Clear();
			}
			RtmpSession[] array2 = array;
			foreach (RtmpSession rtmpSession in array2)
			{
				rtmpSession.Close();
			}
		}

		private void AcceptLoop(TcpListener l)
		{
			while (running)
			{
				TcpClient tcpClient;
				try
				{
					tcpClient = l.AcceptTcpClient();
				}
				catch
				{
					if (running)
					{
						Thread.Sleep(200);
					}
					continue;
				}
				tcpClient.NoDelay = true;
				RtmpSession s = new RtmpSession(this, tcpClient);
				lock (sessions)
				{
					sessions.Add(s);
				}
				Thread thread = new Thread((ThreadStart)delegate
				{
					s.Run();
					lock (sessions)
					{
						sessions.Remove(s);
					}
				});
				thread.IsBackground = true;
				thread.Name = "rtmp-session";
				Thread thread2 = thread;
				thread2.Start();
			}
		}
	}
}
