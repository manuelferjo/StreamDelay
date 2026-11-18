// Avisos que no llegan por el chat público:
// - Follows de Twitch: EventSub por websocket con la conexión de Twitch de la pestaña "Título"
//   (permiso moderator:read:followers; hay que ser el dueño del canal o moderador).
// - Donaciones de StreamElements (servidor "Astro", con el JWT de la cuenta) y de Streamlabs
//   (Socket API, con el "Socket API Token"). Si no hay follows de EventSub, sus avisos de follow
//   de Twitch sirven de alternativa.
using System;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace StreamDelay
{
    // Websocket que se reconecta solo, con espera creciente.
    abstract class WsClient
    {
        protected readonly ChatHub hub;
        protected volatile bool run;
        protected ClientWebSocket ws;
        protected int IdleTimeoutMs;        // >0: si no llega nada en este tiempo, reconectar
        protected int PingMs;               // >0: enviar PingText cada tanto
        protected string PingText;
        Thread th;
        readonly object sendLock = new object();

        protected WsClient(ChatHub h) { hub = h; }

        protected abstract string Name { get; }
        protected abstract string Url();
        protected abstract void OnText(string s);
        protected virtual void Opened() { }
        // El servidor cerró la conexión con este código. Devuelve cuántos ms esperar antes de reintentar
        // (0: la espera normal, que va creciendo). Para no reintentar más, llamar a Fatal().
        protected virtual int Closed(int code, string reason) { return 0; }
        protected abstract void State(string s);

        public void Start() { run = true; th = new Thread(Loop) { IsBackground = true, Name = "chat-" + Name }; th.Start(); }
        public void Stop() { run = false; try { if (ws != null) ws.Abort(); } catch { } }

        // Para dejar de reintentar cuando el fallo no se arregla solo (token incorrecto, sin permiso…).
        protected void Fatal(string s) { State(s); run = false; try { if (ws != null) ws.Abort(); } catch { } }

        void Loop()
        {
            int wait = 2000;
            while (run && hub.Alive)
            {
                int forced = 0;
                try
                {
                    ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    ws.ConnectAsync(new Uri(Url()), CancellationToken.None).Wait(15000);
                    if (ws.State != WebSocketState.Open) throw new Exception("no se pudo conectar");
                    Opened();
                    if (PingMs > 0)
                    {
                        var w = ws;
                        new Thread(() =>
                        {
                            while (run && w.State == WebSocketState.Open)
                            {
                                for (int i = 0; i < PingMs / 100 && run && w.State == WebSocketState.Open; i++) Thread.Sleep(100);
                                if (PingText != null) Send(w, PingText);
                            }
                        }) { IsBackground = true }.Start();
                    }
                    var buf = new byte[64 * 1024];
                    var msg = new MemoryStream();
                    while (run && ws.State == WebSocketState.Open)
                    {
                        WebSocketReceiveResult r;
                        using (var cts = IdleTimeoutMs > 0 ? new CancellationTokenSource(IdleTimeoutMs) : new CancellationTokenSource())
                        {
                            try { r = ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token).Result; }
                            catch (AggregateException) { if (cts.IsCancellationRequested) throw new Exception("sin respuesta del servidor"); throw; }
                        }
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            forced = Closed(ws.CloseStatus.HasValue ? (int)ws.CloseStatus.Value : 0, ws.CloseStatusDescription ?? "");
                            break;
                        }
                        msg.Write(buf, 0, r.Count);
                        if (!r.EndOfMessage) continue;
                        string text = Encoding.UTF8.GetString(msg.ToArray());
                        msg.SetLength(0);
                        wait = 2000;
                        try { OnText(text); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    var e = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                    if (run) State("error: " + e.Message);
                }
                try { if (ws != null) ws.Abort(); } catch { }
                if (!run) break;
                if (forced > 0)
                {
                    for (int i = 0; i < forced / 100 && run && hub.Alive; i++) Thread.Sleep(100);
                    continue;
                }
                Thread.Sleep(wait);
                wait = Math.Min(wait * 2, 30000);
            }
        }

        protected void Send(string s) { Send(ws, s); }

        void Send(ClientWebSocket w, string s)
        {
            try
            {
                var b = Encoding.UTF8.GetBytes(s);
                lock (sendLock) w.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(10000);
            }
            catch { }
        }

        protected static string JsonStr(string s) { return Web.ToJson(s ?? ""); }

        // "5,00 €" a partir de importe y moneda.
        protected static string Money(object amount, string currency)
        {
            double v;
            string s = Convert.ToString(amount, CultureInfo.InvariantCulture);
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return (s + " " + currency).Trim();
            string c = (currency ?? "").ToUpperInvariant();
            string sym = c == "EUR" ? "€" : c == "USD" ? "$" : c == "GBP" ? "£" : c == "MXN" ? "MX$" : c == "ARS" ? "AR$" : c == "BRL" ? "R$" : c;
            return v.ToString("N2", CultureInfo.GetCultureInfo("es-ES")) + " " + sym;
        }
    }
}
