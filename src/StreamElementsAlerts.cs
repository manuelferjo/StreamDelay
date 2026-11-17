using System;
using System.Collections.Generic;

namespace StreamDelay
{
    // Donaciones de StreamElements (servidor "Astro", con el JWT de la cuenta).
    class StreamElementsAlerts : WsClient
    {
        readonly string token;
        public StreamElementsAlerts(ChatHub h, string token) : base(h) { this.token = token.Trim(); }

        protected override string Name { get { return "streamelements"; } }
        protected override void State(string s) { hub.SetAlertState("streamelements", s); }
        protected override string Url() { return "wss://astro.streamelements.com"; }

        protected override void OnText(string s)
        {
            var o = Web.Parse(s);
            string type = J.S(o, "type");
            if (type == "welcome")
                Send("{\"type\":\"subscribe\",\"nonce\":\"" + Guid.NewGuid().ToString("N") + "\",\"data\":{\"topic\":\"channel.activities\",\"token\":" + JsonStr(token) + ",\"token_type\":\"jwt\"}}");
            else if (type == "response")
            {
                string err = J.S(o, "error");
                if (err == "err_unauthorized") Fatal("token incorrecto o caducado");
                else if (err.Length > 0) State("error: " + err);
                else State("conectado");
            }
            else if (type == "message" && J.S(o, "topic") == "channel.activities")
                Activity(J.At(o, "data"));
        }

        void Activity(object a)
        {
            string type = J.S(a, "type");
            var d = J.At(a, "data");
            string user = J.S(d, "displayName");
            if (user.Length == 0) user = J.S(d, "username");
            if (type == "tip")
            {
                if (user.Length == 0) user = "Anónimo";
                string amt = Money(J.At(d, "amount"), J.S(d, "currency"));
                var it = new ChatItem { t = "ev", p = "tip", kind = "tip", user = user, amount = amt, text = user + " ha donado " + amt };
                string m = J.S(d, "message");
                if (m.Length > 0) { it.parts = new List<ChatPart>(); Emotes.AddText(it.parts, m, hub.Emotes.For("twitch")); }
                hub.Emit(it);
            }
            else if (type == "follower" && J.S(a, "provider") == "twitch" && !hub.TwitchFollowsActive)
                hub.Emit(new ChatItem { t = "ev", p = "twitch", kind = "follow", user = user, text = user + " te sigue" });
        }
    }
}
