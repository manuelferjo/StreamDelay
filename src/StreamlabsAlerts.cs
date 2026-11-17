using System;
using System.Collections.Generic;

namespace StreamDelay
{
    // Donaciones de Streamlabs (Socket API, con el "Socket API Token").
    class StreamlabsAlerts : WsClient
    {
        readonly string token;
        public StreamlabsAlerts(ChatHub h, string token) : base(h) { this.token = token.Trim(); PingMs = 10000; PingText = "2"; }

        protected override string Name { get { return "streamlabs"; } }
        protected override void State(string s) { hub.SetAlertState("streamlabs", s); }
        protected override string Url() { return "wss://sockets.streamlabs.com/socket.io/?token=" + Uri.EscapeDataString(token) + "&EIO=3&transport=websocket"; }

        protected override void OnText(string s)
        {
            if (s.StartsWith("0{"))
            {
                long ping = J.N(Web.Parse(s.Substring(1)), 25000, "pingInterval");
                PingMs = (int)Math.Max(5000, Math.Min(ping, 25000));
            }
            else if (s == "40") State("conectado");
            else if (s.StartsWith("44")) Fatal("token incorrecto");
            else if (s.StartsWith("42"))
            {
                var arr = Web.Parse(s.Substring(2));
                if (J.S(arr, 0) != "event") return;
                var ev = J.At(arr, 1);
                string type = J.S(ev, "type");
                foreach (var m in J.L(ev, "message"))
                {
                    if (type == "donation")
                    {
                        string user = J.S(m, "name");
                        if (user.Length == 0) user = "Anónimo";
                        string amt = J.S(m, "formatted_amount");
                        if (amt.Length == 0) amt = Money(J.At(m, "amount"), J.S(m, "currency"));
                        var it = new ChatItem { t = "ev", p = "tip", kind = "tip", user = user, amount = amt, text = user + " ha donado " + amt };
                        string msg = J.S(m, "message");
                        if (msg.Length > 0) { it.parts = new List<ChatPart>(); Emotes.AddText(it.parts, msg, hub.Emotes.For("twitch")); }
                        hub.Emit(it);
                    }
                    else if (type == "follow" && J.S(ev, "for") == "twitch_account" && !hub.TwitchFollowsActive)
                    {
                        string user = J.S(m, "name");
                        if (user.Length > 0) hub.Emit(new ChatItem { t = "ev", p = "twitch", kind = "follow", user = user, text = user + " te sigue" });
                    }
                }
            }
        }
    }
}
