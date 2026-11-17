using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace StreamDelay
{
    // Chat de YouTube: la API interna de la web (sin clave ni cupo). Mensajes, Super Chats, miembros y regalos.
    class YouTubeChat
    {
        readonly ChatHub hub;
        readonly string input;
        volatile bool run;
        Thread th;
        volatile string videoId = "", clientVersion = "2.20250101.00.00";
        string channelId = "";

        public YouTubeChat(ChatHub h, string s) { hub = h; input = s; }

        public void Start() { run = true; th = new Thread(Loop) { IsBackground = true, Name = "chat-youtube" }; th.Start(); }
        public void Stop() { run = false; }

        // Acepta: @canal, enlace del canal, ID de canal (UC…), enlace del directo o ID del vídeo.
        string PageUrl()
        {
            string s = input.Trim();
            var m = Regex.Match(s, @"(?:v=|youtu\.be/|/live/|/shorts/)([\w-]{11})");
            if (m.Success) return "https://www.youtube.com/watch?v=" + m.Groups[1].Value;
            if (Regex.IsMatch(s, @"^[\w-]{11}$") && !s.StartsWith("@")) return "https://www.youtube.com/watch?v=" + s;
            m = Regex.Match(s, @"(UC[\w-]{22})");
            if (m.Success) return "https://www.youtube.com/channel/" + m.Groups[1].Value + "/live";
            m = Regex.Match(s, @"@([\w.\-]+)");
            if (m.Success) return "https://www.youtube.com/@" + m.Groups[1].Value + "/live";
            m = Regex.Match(s, @"youtube\.com/(c/|user/)?([\w.\-]+)");
            if (m.Success) return "https://www.youtube.com/" + m.Groups[1].Value + m.Groups[2].Value + "/live";
            return "https://www.youtube.com/@" + s.TrimStart('@') + "/live";
        }

        void Loop()
        {
            while (run && hub.Alive)
            {
                string cont = null;
                try
                {
                    string html = Web.Get(PageUrl(), Web.YouTubeCookies);
                    var mv = Regex.Match(html, "<link rel=\"canonical\" href=\"https://www\\.youtube\\.com/watch\\?v=([\\w-]{11})\"");
                    var mc = Regex.Match(html, "\"INNERTUBE_CLIENT_VERSION\":\"([^\"]+)\"");
                    var mk = Regex.Match(html, "\"reloadContinuationData\":\\{\"continuation\":\"([^\"]+)\"");
                    var mch = Regex.Match(html, "\"channelId\":\"(UC[\\w-]{22})\"");
                    if (mc.Success) clientVersion = mc.Groups[1].Value;
                    if (mch.Success && mch.Groups[1].Value != channelId)
                    {
                        channelId = mch.Groups[1].Value;
                        string cid = channelId;
                        ChatHub.Background(() => hub.Emotes.LoadYouTube(cid, hub.Log));
                    }
                    if (html.Contains("consent.youtube.com/") && !mc.Success) throw new Exception("YouTube pide aceptar cookies; vuelve a intentarlo");
                    if (!mv.Success || !mk.Success || !html.Contains("\"isLiveNow\":true"))
                    {
                        videoId = "";
                        hub.SetState("youtube", "sin directo");
                    }
                    else
                    {
                        videoId = mv.Groups[1].Value;
                        cont = LiveChatContinuation(videoId) ?? mk.Groups[1].Value;
                    }
                }
                catch (Exception ex)
                {
                    hub.SetState("youtube", "error: " + ex.Message);
                }
                if (cont != null)
                {
                    hub.SetState("youtube", "conectado");
                    Poll(cont);
                }
                // Sin directo (o se terminó): volver a mirar cada 30 s.
                for (int i = 0; i < 300 && run; i++) Thread.Sleep(100);
            }
        }

        // YouTube tiene dos chats: "Chat destacado" (filtrado, el de por defecto) y "Chat en directo" (todos
        // los mensajes). La página del chat emergente trae la continuación de los dos; el segundo del menú
        // es el completo (así funciona aunque YouTube esté en otro idioma).
        string LiveChatContinuation(string v)
        {
            try
            {
                string html = Web.Get("https://www.youtube.com/live_chat?is_popout=1&v=" + v, Web.YouTubeCookies);
                var items = Regex.Matches(html, "\"selected\":(?:true|false),\"continuation\":\\{\"reloadContinuationData\":\\{\"continuation\":\"([^\"]+)\"");
                if (items.Count >= 2) return items[1].Groups[1].Value;
                if (items.Count == 1) return items[0].Groups[1].Value;
            }
            catch { }
            return null;
        }

        string Ctx() { return "{\"client\":{\"clientName\":\"WEB\",\"clientVersion\":\"" + clientVersion + "\",\"hl\":\"es\",\"gl\":\"ES\"}}"; }

        void Poll(string cont)
        {
            bool first = true;
            int errors = 0;
            while (run && hub.Alive && cont != null)
            {
                int timeout = 3000;
                try
                {
                    var o = Web.PostJson("https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?prettyPrint=false",
                        "{\"context\":" + Ctx() + ",\"continuation\":\"" + cont + "\"}", null, Web.YouTubeCookies);
                    var lc = J.At(o, "continuationContents", "liveChatContinuation");
                    if (lc == null) { hub.SetState("youtube", "sin directo"); return; }
                    string next = null;
                    foreach (var c in J.L(lc, "continuations"))
                    {
                        var cd = J.At(c, "invalidationContinuationData") ?? J.At(c, "timedContinuationData") ?? J.At(c, "reloadContinuationData");
                        if (cd == null) continue;
                        next = J.S(cd, "continuation");
                        timeout = (int)J.N(cd, 3000, "timeoutMs");
                    }
                    // La primera respuesta trae el historial reciente: no se repite en pantalla.
                    if (!first) foreach (var a in J.L(lc, "actions")) Action(a);
                    first = false;
                    cont = next;
                    errors = 0;
                }
                catch (Exception ex)
                {
                    if (++errors >= 5) { hub.SetState("youtube", "error: " + ex.Message); return; }
                }
                timeout = Math.Max(1000, Math.Min(timeout, 8000));
                for (int i = 0; i < timeout / 100 && run; i++) Thread.Sleep(100);
            }
            hub.SetState("youtube", "sin directo");
        }

        void Action(object a)
        {
            var item = J.At(a, "addChatItemAction", "item");
            if (item != null)
            {
                object r;
                if ((r = J.At(item, "liveChatTextMessageRenderer")) != null) hub.Emit(Base(r, "msg", null));
                else if ((r = J.At(item, "liveChatPaidMessageRenderer")) != null)
                {
                    var it = Base(r, "ev", "superchat");
                    it.amount = J.Text(J.At(r, "purchaseAmountText"));
                    it.text = it.user + " ha enviado un Super Chat de " + it.amount;
                    it.color = Argb(J.N(r, 0, "bodyBackgroundColor"));
                    hub.Emit(it);
                }
                else if ((r = J.At(item, "liveChatPaidStickerRenderer")) != null)
                {
                    var it = Base(r, "ev", "sticker");
                    it.amount = J.Text(J.At(r, "purchaseAmountText"));
                    it.text = it.user + " ha enviado un Super Sticker de " + it.amount;
                    it.parts = null;
                    hub.Emit(it);
                }
                else if ((r = J.At(item, "liveChatMembershipItemRenderer")) != null)
                {
                    var it = Base(r, "ev", "member");
                    string head = J.Text(J.At(r, "headerPrimaryText"));
                    string sub = J.Text(J.At(r, "headerSubtext"));
                    it.text = it.user + ": " + (head.Length > 0 ? head : sub.Length > 0 ? sub : "nuevo miembro");
                    hub.Emit(it);
                }
                else if ((r = J.At(item, "liveChatSponsorshipsGiftPurchaseAnnouncementRenderer")) != null)
                {
                    var h = J.At(r, "header", "liveChatSponsorshipsHeaderRenderer");
                    var it = new ChatItem { t = "ev", p = "youtube", kind = "giftmember", id = J.S(r, "id"), user = J.Text(J.At(h, "authorName")) };
                    it.text = it.user + " " + J.Text(J.At(h, "primaryText"));
                    hub.Emit(it);
                }
                return;
            }
            string del = J.S(a, "markChatItemAsDeletedAction", "targetItemId");
            if (del.Length > 0) { hub.Emit(new ChatItem { t = "del", p = "youtube", id = del }); return; }
            string ban = J.S(a, "markChatItemsByAuthorAsDeletedAction", "externalChannelId");
            if (ban.Length > 0) hub.Emit(new ChatItem { t = "del", p = "youtube", uid = ban });
        }

        ChatItem Base(object r, string t, string kind)
        {
            var it = new ChatItem
            {
                t = t, p = "youtube", kind = kind, id = J.S(r, "id"), uid = J.S(r, "authorExternalChannelId"),
                user = J.Text(J.At(r, "authorName")).TrimStart('@')
            };
            var roles = new List<string>();
            foreach (var b in J.L(r, "authorBadges"))
            {
                var br = J.At(b, "liveChatAuthorBadgeRenderer");
                string icon = J.S(br, "icon", "iconType");
                if (icon == "OWNER") roles.Add("broadcaster");
                else if (icon == "MODERATOR") roles.Add("mod");
                else if (icon == "VERIFIED") roles.Add("verified");
                else if (J.At(br, "customThumbnail") != null) roles.Add("member");
            }
            it.roles = roles.ToArray();
            var parts = new List<ChatPart>();
            var map = hub.Emotes.For("youtube");
            foreach (var run in J.L(r, "message", "runs"))
            {
                var emoji = J.At(run, "emoji");
                if (emoji == null) { Emotes.AddText(parts, J.S(run, "text"), map); continue; }
                bool custom = J.S(emoji, "isCustomEmoji").ToLowerInvariant() == "true";
                var thumbs = J.L(emoji, "image", "thumbnails");
                string name = J.S(emoji, "shortcuts", 0);
                if (custom && thumbs.Count > 0) parts.Add(new ChatPart { e = J.S(thumbs[thumbs.Count - 1], "url"), n = name });
                else parts.Add(new ChatPart { x = J.S(emoji, "emojiId") });
            }
            if (parts.Count > 0) it.parts = parts;
            return it;
        }

        static string Argb(long v)
        {
            if (v == 0) return null;
            return "#" + ((v >> 16) & 0xFF).ToString("X2") + ((v >> 8) & 0xFF).ToString("X2") + (v & 0xFF).ToString("X2");
        }

        public int FetchViewers()
        {
            string v = videoId;
            if (v.Length == 0) return -2;
            var o = Web.PostJson("https://www.youtube.com/youtubei/v1/updated_metadata?prettyPrint=false",
                "{\"context\":" + Ctx() + ",\"videoId\":\"" + v + "\"}", null, Web.YouTubeCookies);
            foreach (var a in J.L(o, "actions"))
            {
                string n = J.S(a, "updateViewershipAction", "viewCount", "videoViewCountRenderer", "originalViewCount");
                long r;
                if (long.TryParse(n, out r)) return (int)r;
            }
            return -1;
        }
    }
}
