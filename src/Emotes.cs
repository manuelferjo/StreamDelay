using System;
using System.Collections.Generic;
using System.Text;

namespace StreamDelay
{
    // Emotes de 7TV, BetterTTV y FrankerFaceZ.
    class Emotes
    {
        readonly object lk = new object();
        Dictionary<string, string> global = new Dictionary<string, string>();
        Dictionary<string, string> twitchCh = new Dictionary<string, string>();
        Dictionary<string, string> kickCh = new Dictionary<string, string>();
        Dictionary<string, string> ytCh = new Dictionary<string, string>();
        Dictionary<string, string> mergedTw, mergedKick, mergedYt;
        string twitchId = "", kickId = "", ytId = "";

        // Emotes para una plataforma: globales + los del canal de Twitch (muchos streamers solo tienen ahí
        // su set de 7TV/BTTV/FFZ) + los de 7TV de esa plataforma.
        public Dictionary<string, string> For(string p)
        {
            lock (lk)
            {
                if (p == "twitch") return mergedTw ?? (mergedTw = Merge(global, twitchCh, null));
                if (p == "kick") return mergedKick ?? (mergedKick = Merge(global, twitchCh, kickCh));
                return mergedYt ?? (mergedYt = Merge(global, twitchCh, ytCh));
            }
        }

        static Dictionary<string, string> Merge(Dictionary<string, string> a, Dictionary<string, string> b, Dictionary<string, string> c)
        {
            var r = new Dictionary<string, string>(a);
            foreach (var kv in b) r[kv.Key] = kv.Value;
            if (c != null) foreach (var kv in c) r[kv.Key] = kv.Value;
            return r;
        }

        void Set(ref Dictionary<string, string> target, Dictionary<string, string> v)
        {
            lock (lk) { target = v; mergedTw = mergedKick = mergedYt = null; }
        }

        public void LoadGlobal(Action<string> log)
        {
            var d = new Dictionary<string, string>();
            int n7 = Add7tv(d, "https://7tv.io/v3/emote-sets/global", "emotes");
            int nb = AddBttv(d, "https://api.betterttv.net/3/cached/emotes/global", null);
            int nf = AddFfz(d, "https://api.betterttv.net/3/cached/frankerfacez/emotes/global");
            Set(ref global, d);
            log("Emotes globales cargados: 7TV " + n7 + ", BTTV " + nb + ", FFZ " + nf + ".");
        }

        public void LoadTwitch(string id, Action<string> log)
        {
            if (id.Length == 0 || id == twitchId) return;
            twitchId = id;
            var d = new Dictionary<string, string>();
            int n7 = Add7tv(d, "https://7tv.io/v3/users/twitch/" + id, "emote_set", "emotes");
            int nb = AddBttv(d, "https://api.betterttv.net/3/cached/users/twitch/" + id, "channelEmotes")
                   + AddBttv(d, "https://api.betterttv.net/3/cached/users/twitch/" + id, "sharedEmotes");
            int nf = AddFfz(d, "https://api.betterttv.net/3/cached/frankerfacez/users/twitch/" + id);
            Set(ref twitchCh, d);
            log("Emotes del canal de Twitch: 7TV " + n7 + ", BTTV " + nb + ", FFZ " + nf + ".");
        }

        public void LoadKick(string userId, Action<string> log)
        {
            if (userId.Length == 0 || userId == kickId) return;
            kickId = userId;
            var d = new Dictionary<string, string>();
            int n = Add7tv(d, "https://7tv.io/v3/users/kick/" + userId, "emote_set", "emotes");
            Set(ref kickCh, d);
            if (n > 0) log("Emotes de 7TV del canal de Kick: " + n + ".");
        }

        public void LoadYouTube(string channelId, Action<string> log)
        {
            if (channelId.Length == 0 || channelId == ytId) return;
            ytId = channelId;
            var d = new Dictionary<string, string>();
            int n = Add7tv(d, "https://7tv.io/v3/users/youtube/" + channelId, "emote_set", "emotes");
            Set(ref ytCh, d);
            if (n > 0) log("Emotes de 7TV del canal de YouTube: " + n + ".");
        }

        static int Add7tv(Dictionary<string, string> d, string url, params object[] path)
        {
            try
            {
                int n = 0;
                foreach (var e in J.L(Web.GetJson(url), path))
                {
                    string name = J.S(e, "name"), host = J.S(e, "data", "host", "url");
                    if (name.Length == 0 || host.Length == 0) continue;
                    d[name] = (host.StartsWith("//") ? "https:" : "") + host + "/2x.webp";
                    n++;
                }
                return n;
            }
            catch { return 0; }
        }

        static int AddBttv(Dictionary<string, string> d, string url, string key)
        {
            try
            {
                object o = Web.GetJson(url);
                int n = 0;
                foreach (var e in key == null ? J.L(o) : J.L(o, key))
                {
                    string code = J.S(e, "code"), id = J.S(e, "id");
                    if (code.Length == 0 || id.Length == 0) continue;
                    d[code] = "https://cdn.betterttv.net/emote/" + id + "/2x";
                    n++;
                }
                return n;
            }
            catch { return 0; }
        }

        static int AddFfz(Dictionary<string, string> d, string url)
        {
            try
            {
                int n = 0;
                foreach (var e in J.L(Web.GetJson(url)))
                {
                    string code = J.S(e, "code");
                    string img = J.S(e, "images", "2x");
                    if (img.Length == 0) img = J.S(e, "images", "1x");
                    if (code.Length == 0 || img.Length == 0) continue;
                    d[code] = img;
                    n++;
                }
                return n;
            }
            catch { return 0; }
        }

        // Para el botón de emotes del chat: los de los canales (Twitch, Kick y YouTube) o los globales.
        public List<KeyValuePair<string, string>> List(bool channel)
        {
            var r = new List<KeyValuePair<string, string>>();
            lock (lk)
            {
                if (channel)
                {
                    var seen = new HashSet<string>();
                    foreach (var d in new[] { twitchCh, kickCh, ytCh })
                        foreach (var kv in d) if (seen.Add(kv.Key)) r.Add(kv);
                }
                else r.AddRange(global);
            }
            r.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return r;
        }

        // Añade texto a las partes, cambiando por imagen las palabras que sean emotes de 7TV/BTTV/FFZ.
        public static void AddText(List<ChatPart> parts, string text, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(text)) return;
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                int j = text.IndexOf(' ', i);
                if (j < 0) j = text.Length;
                string w = text.Substring(i, j - i);
                string url;
                if (w.Length > 0 && map != null && map.TryGetValue(w, out url))
                {
                    if (sb.Length > 0) { parts.Add(new ChatPart { x = sb.ToString() }); sb.Length = 0; }
                    parts.Add(new ChatPart { e = url, n = w });
                }
                else sb.Append(w);
                if (j < text.Length) sb.Append(' ');
                i = j + 1;
            }
            if (sb.Length > 0) parts.Add(new ChatPart { x = sb.ToString() });
        }
    }
}
