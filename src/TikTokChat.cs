// Chat de TikTok LIVE. TikTok no tiene una API pública para leer el chat, así que se usa
// Euler Stream (eulerstream.com), un servicio que se conecta al directo y reenvía sus eventos
// como JSON por un websocket. Hace falta una clave gratuita de Euler Stream (plan Community).
// Llegan: mensajes (con emotes de TikTok), regalos, suscripciones, follows, espectadores y el
// fin del directo. TikTok no deja escribir en el chat desde otros programas.
//
// Mensajes del websocket (por defecto van agrupados):
//   {"messages":[{"type":"WebcastChatMessage","data":{...}}, ...]}
// "data" es el mensaje de TikTok decodificado; los nombres de los campos cambian un poco según la
// versión del esquema, así que se leen con alternativas (displayId/uniqueId, content/comment…).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace StreamDelay
{
    class TikTokChat : WsClient
    {
        readonly string user, key;
        int offline;                 // cuántas veces seguidas no estaba en directo
        bool live;

        public TikTokChat(ChatHub h, string uniqueId, string apiKey) : base(h)
        {
            user = uniqueId;
            key = (apiKey ?? "").Trim();
            // Si en 3 minutos no llega nada (ni espectadores), la conexión se ha quedado colgada.
            IdleTimeoutMs = 180000;
        }

        protected override string Name { get { return "tiktok"; } }

        protected override string Url()
        {
            return "wss://ws.eulerstream.com?uniqueId=" + Uri.EscapeDataString(user) + "&apiKey=" + Uri.EscapeDataString(key);
        }

        protected override void State(string s)
        {
            // Si Euler Stream rechaza la clave al conectar, no se reintenta (cada intento gasta cupo).
            if (s.StartsWith("error") && (s.Contains("'401'") || s.Contains("'403'")))
            {
                run = false;
                try { if (ws != null) ws.Abort(); } catch { }
                s = "error: la clave de Euler Stream no es válida";
            }
            hub.SetState("tiktok", s);
        }

        protected override void OnText(string s) { Handle(s); }

        // Códigos de cierre de Euler Stream.
        protected override int Closed(int code, string reason)
        {
            switch (code)
            {
                case 4404:      // no está en directo
                case 4005:      // el directo ha terminado
                    NotLive();
                    // Cada intento gasta peticiones del plan gratuito: sin directo se mira cada 2 min
                    // y, tras media hora, cada 5 min.
                    return offline > 15 ? 300000 : 120000;
                case 4401:
                    Fatal("error: la clave de Euler Stream no es válida");
                    return 0;
                case 4403:
                    Fatal("error: tu plan de Euler Stream no permite esta conexión");
                    return 0;
                case 4400:
                    Fatal("error: el usuario de TikTok \"" + user + "\" no es válido");
                    return 0;
                case 4429:
                    State("error: demasiadas conexiones con esta clave de Euler Stream");
                    return 60000;
                case 4555:      // Euler Stream corta cada 8 horas: se vuelve a conectar al momento
                    return 1000;
                default:
                    // 1000/1001: cierre normal, se vuelve a conectar sin avisar de nada.
                    if (code >= 4000 && reason.Length > 0) State("error: " + reason);
                    return 0;
            }
        }

        void NotLive()
        {
            offline++;
            live = false;
            hub.TikTokViewers = -2;
            hub.ViewersChanged();
            State("sin directo");
        }

        void Live()
        {
            offline = 0;
            if (!live)
            {
                live = true;
                State("conectado");
            }
        }

        // Un texto del websocket: un evento suelto o un paquete con varios.
        internal void Handle(string text)
        {
            object o;
            try { o = Web.Parse(text); }
            catch { return; }
            var list = J.L(o, "messages");
            if (list.Count > 0) { foreach (var m in list) Event(J.S(m, "type"), J.At(m, "data")); return; }
            if (J.S(o, "type").Length > 0) Event(J.S(o, "type"), J.At(o, "data"));
        }

        void Event(string type, object d)
        {
            switch (type)
            {
                case "tiktok.connect":
                    Live();
                    break;
                case "room.status":
                    {
                        string st = J.S(d, "state");
                        if (st == "connected") Live();
                        else if (st == "offline" || st == "ended") NotLive();
                    }
                    break;
                case "WebcastChatMessage": Live(); Chat(d); break;
                case "WebcastEmoteChatMessage": Live(); Sticker(d); break;
                case "WebcastGiftMessage": Live(); Gift(d); break;
                case "WebcastSubNotifyMessage":
                    {
                        string u = Nick(J.At(d, "user"));
                        long months = J.N(d, 1, "subMonth");
                        Ev("sub", u, months > 1 ? u + " lleva " + months + " meses suscrito" : u + " se ha suscrito", null);
                    }
                    break;
                case "WebcastSocialMessage":
                    {
                        // Follow o compartir: se distingue por el texto que TikTok enseña en el chat.
                        string k = (J.S(d, "common", "displayText", "key") + " " + J.S(d, "displayType")).ToLowerInvariant();
                        if (k.Contains("follow"))
                        {
                            string u = Nick(J.At(d, "user"));
                            if (u.Length > 0) Ev("follow", u, u + " te sigue", null);
                        }
                    }
                    break;
                case "WebcastRoomUserSeqMessage":
                    {
                        Live();
                        long v = J.N(d, -1, "viewerCount");
                        if (v < 0) v = J.N(d, -1, "total");
                        if (v >= 0 && v < int.MaxValue)
                        {
                            hub.TikTokViewers = (int)v;
                            hub.ViewersChanged();
                        }
                    }
                    break;
                case "WebcastControlMessage":
                    {
                        // 3: el directo ha terminado; 4: TikTok lo ha cortado (puede llegar como número o como nombre).
                        string a = J.S(d, "action").ToUpperInvariant();
                        if (a == "3" || a == "4" || a.Contains("ENDED") || a.Contains("SUSPENDED")) NotLive();
                    }
                    break;
                case "WebcastImDeleteMessage":
                    {
                        foreach (var id in Ids(d, "deleteMsgIdsList", "deleteMsgIds")) hub.Emit(new ChatItem { t = "del", p = "tiktok", id = id });
                        foreach (var id in Ids(d, "deleteUserIdsList", "deleteUserIds")) hub.Emit(new ChatItem { t = "del", p = "tiktok", uid = id });
                    }
                    break;
            }
        }

        static List<string> Ids(object d, string a, string b)
        {
            var r = new List<string>();
            var l = J.L(d, a);
            if (l.Count == 0) l = J.L(d, b);
            foreach (var x in l)
            {
                string s = Convert.ToString(x, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(s)) r.Add(s);
            }
            return r;
        }

        // ---------- Usuarios ----------

        static string First(object o, params string[] names)
        {
            foreach (var n in names)
            {
                object v = J.At(o, n);
                if (v == null || (v is IEnumerable && !(v is string))) continue;
                string s = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (s.Length > 0) return s;
            }
            return "";
        }

        // Nombre que se ve en TikTok (apodo); si no hay, el @usuario.
        static string Nick(object u)
        {
            string s = First(u, "nickname", "nickName");
            return s.Length > 0 ? s : First(u, "displayId", "uniqueId", "username");
        }

        static string UserId(object u) { return First(u, "id", "userId", "idStr"); }

        static string[] Roles(object d)
        {
            object id = J.At(d, "userIdentity");
            if (id == null) id = J.At(d, "UserIdentity");
            var r = new List<string>();
            if (Bool(id, "isAnchor")) r.Add("broadcaster");
            if (Bool(id, "isModeratorOfAnchor")) r.Add("mod");
            if (Bool(id, "isSubscriberOfAnchor")) r.Add("sub");
            return r.ToArray();
        }

        static bool Bool(object o, string name)
        {
            object v = J.At(o, name);
            if (v is bool) return (bool)v;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        // Primera url de una imagen de TikTok (url[], urlList[] o imageUrl).
        static string ImageUrl(object img)
        {
            foreach (var n in new[] { "url", "urlList", "urlListList" })
            {
                var l = J.L(img, n);
                if (l.Count > 0) return Convert.ToString(l[0], CultureInfo.InvariantCulture);
            }
            return First(img, "imageUrl", "url");
        }

        // ---------- Mensajes ----------

        void Chat(object d)
        {
            object u = J.At(d, "user");
            string content = First(d, "content", "comment");
            var map = hub.Emotes.For("tiktok");
            var parts = new List<ChatPart>();
            // Emotes de TikTok: van aparte, con la posición del texto donde se ponen.
            var emotes = J.L(d, "emotes");
            if (emotes.Count == 0) emotes = J.L(d, "emotesList");
            var placed = new List<KeyValuePair<int, ChatPart>>();
            foreach (var e in emotes)
            {
                object em = J.At(e, "emote");
                string url = ImageUrl(J.At(em, "image"));
                if (url.Length == 0) url = First(e, "emoteImageUrl");
                if (url.Length == 0) continue;
                long idx = J.N(e, -1, "index");
                if (idx < 0) idx = J.N(e, -1, "placeInComment");
                if (idx < 0 || idx > content.Length) idx = content.Length;
                placed.Add(new KeyValuePair<int, ChatPart>((int)idx, new ChatPart { e = url, n = "emote" }));
            }
            placed.Sort((a, b) => a.Key.CompareTo(b.Key));
            int pos = 0;
            foreach (var pe in placed)
            {
                if (pe.Key > pos) Emotes.AddText(parts, content.Substring(pos, pe.Key - pos), map);
                pos = Math.Max(pos, pe.Key);
                parts.Add(pe.Value);
            }
            if (pos < content.Length) Emotes.AddText(parts, content.Substring(pos), map);
            if (parts.Count == 0) return;
            hub.Emit(new ChatItem
            {
                t = "msg", p = "tiktok", id = J.S(d, "common", "msgId"), uid = UserId(u),
                user = Nick(u), roles = Roles(d), parts = parts
            });
        }

        // Mensaje que es solo un sticker (emote) del canal.
        void Sticker(object d)
        {
            object u = J.At(d, "user");
            var parts = new List<ChatPart>();
            var list = J.L(d, "emoteList");
            if (list.Count == 0) list = J.L(d, "emoteListList");
            foreach (var e in list)
            {
                string url = ImageUrl(J.At(e, "image"));
                if (url.Length > 0) parts.Add(new ChatPart { e = url, n = "emote" });
            }
            if (parts.Count == 0) return;
            hub.Emit(new ChatItem
            {
                t = "msg", p = "tiktok", id = J.S(d, "common", "msgId"), uid = UserId(u),
                user = Nick(u), roles = Roles(d), parts = parts
            });
        }

        // Regalos: los que se pueden mandar en racha (tipo 1) solo se avisan al acabar la racha.
        void Gift(object d)
        {
            object g = J.At(d, "gift");
            if (g == null) g = J.At(d, "giftDetails");
            long type = J.N(g, 0, "type");
            if (type == 0) type = J.N(g, 0, "giftType");
            bool end = Bool(d, "repeatEnd");
            if (type == 1 && !end) return;
            string name = First(g, "name", "giftName", "describe");
            if (name.Length == 0) name = "un regalo";
            long count = Math.Max(1, J.N(d, 1, "repeatCount"));
            long diamonds = J.N(g, 0, "diamondCount") * count;
            string u = Nick(J.At(d, "user"));
            if (u.Length == 0) u = "Alguien";
            string what = count > 1 ? count + " × " + name : name;
            Ev("tiktokgift", u, u + " ha enviado " + what, diamonds > 0 ? "💎 " + diamonds.ToString("N0", CultureInfo.GetCultureInfo("es-ES")) : null);
        }

        void Ev(string kind, string u, string text, string amount)
        {
            hub.Emit(new ChatItem { t = "ev", p = "tiktok", kind = kind, user = u, text = text, amount = amount });
        }
    }
}
