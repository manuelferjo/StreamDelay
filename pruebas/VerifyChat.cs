// Banco de pruebas: pasa los mismos datos de ejemplo por las clases del chat de un StreamDelay.exe
// (cargado por reflexión) e imprime todo lo que producen. Se ejecuta con la 3.0 y con la versión
// con el código original, y se comparan las salidas: deben ser idénticas.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

static class VerifyChat
{
    static Assembly A;
    static object hub;
    static readonly StringBuilder Out = new StringBuilder();
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    static Type T(string n) { return A.GetType("StreamDelay." + n, true); }
    static object Call(object o, string m, params object[] args)
    {
        var t = o as Type ?? o.GetType();
        var mi = t.GetMethods(F).First(x => x.Name == m && x.GetParameters().Length == args.Length);
        try { return mi.Invoke(o is Type ? null : o, args); }
        catch (TargetInvocationException e) { Out.AppendLine("  EXCEPCIÓN " + m + ": " + e.InnerException.GetType().Name + ": " + e.InnerException.Message); return null; }
    }
    static void SetField(object o, string f, object v) { o.GetType().GetField(f, F).SetValue(o, v); }
    static object Make(string type, params object[] args) { return Activator.CreateInstance(T(type), F, null, args, null); }

    static string Dump(object o)
    {
        if (o == null) return "null";
        if (o is string) return "\"" + o + "\"";
        if (o is IEnumerable && !(o is string)) { var l = new List<string>(); foreach (var x in (IEnumerable)o) l.Add(Dump(x)); return "[" + string.Join(", ", l) + "]"; }
        var t = o.GetType();
        if (t.Namespace == "StreamDelay")
            return "{" + string.Join(" ", t.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name).Select(f => f.Name + "=" + Dump(f.GetValue(o)))) + "}";
        return Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
    }

    static void Section(string s) { Out.AppendLine("== " + s); }

    static int Main(string[] a)
    {
        A = Assembly.LoadFile(Path.GetFullPath(a[0]));
        hub = Make("ChatHub");
        // Emitir y registrar: todo queda en la salida.
        var itemT = typeof(Action<>).MakeGenericType(T("ChatItem"));
        var logField = T("ChatHub").GetField("Log", F); var itemField = T("ChatHub").GetField("Item", F);
        Action<string> log = s => Out.AppendLine("  log: " + s);
        logField.SetValue(hub, log);
        var sink = typeof(VerifyChat).GetMethod("OnItem", F).MakeGenericMethod(T("ChatItem"));
        itemField.SetValue(hub, Delegate.CreateDelegate(itemT, sink));
        // Emotes de terceros de prueba (sin internet): 7TV "KEKW" y "Pog".
        var em = T("ChatHub").GetField("Emotes", F).GetValue(hub);
        var fake = new Dictionary<string, string> { { "KEKW", "https://cdn.7tv.app/emote/K/2x.webp" }, { "Pog", "https://cdn.betterttv.net/emote/P/2x" } };
        SetField(em, "global", fake);

        Section("Emotes.AddText / For");
        foreach (var txt in new[] { "hola KEKW que tal", "KEKW", " Pog  Pog ", "", "sin emotes aquí", "KEKWKEKW Pog." })
        {
            var parts = Activator.CreateInstance(typeof(List<>).MakeGenericType(T("ChatPart")));
            Call(T("Emotes"), "AddText", parts, txt, fake); Out.AppendLine("  " + Dump(parts));
        }
        Out.AppendLine("  For(kick): " + Dump(((IDictionary)Call(em, "For", "kick")).Count));

        Section("J");
        var js = Json.DeserializeObject("{\"a\":{\"b\":[1,{\"c\":\"x\"}]},\"runs\":[{\"text\":\"ho\"},{\"text\":\"la\"}],\"simpleText\":\"\"}");
        Out.AppendLine("  " + Dump(Call(T("J"), "S", js, new object[] { "a", "b", 1, "c" })) + " " + Dump(Call(T("J"), "N", js, 7L, new object[] { "a", "b", 0 })) + " " + Dump(Call(T("J"), "Text", js)) + " " + Dump(Call(T("J"), "L", js, new object[] { "runs" })));

        Section("ChatHub.Clean / SetState / SetAlertState");
        foreach (var c in new[] { "  @Ibai ", "https://twitch.tv/Ibai/", "#canal", "kick.com/xqc", "", null }) Out.AppendLine("  " + Dump(Call(T("ChatHub"), "Clean", c)));
        Call(hub, "SetState", "twitch", "conectado");   // sin arrancar: no hace nada
        SetField(hub, "running", true);
        Call(hub, "SetState", "twitch", "conectado"); Call(hub, "SetState", "twitch", "conectado"); Call(hub, "SetState", "kick", "sin directo"); Call(hub, "SetState", "youtube", "error: x");
        Call(hub, "SetAlertState", "follows", "activo"); Call(hub, "SetAlertState", "streamlabs", "token incorrecto"); Call(hub, "SetAlertState", "streamelements", "conectado");
        Out.AppendLine("  TwitchFollowsActive=" + T("ChatHub").GetProperty("TwitchFollowsActive").GetValue(hub, null));
        SetField(hub, "FollowsState", "");

        Section("TwitchChat");
        var tw = Make("TwitchChat", hub, "canal");
        foreach (var s in new[] { "hola\\sque\\:tal\\\\", "sin escapes", "a\\", "\\r\\n" }) Out.AppendLine("  Unescape " + Dump(Call(T("TwitchChat"), "Unescape", s)));
        foreach (var b in new[] { "broadcaster/1,subscriber/12", "moderator/1,vip/1,founder/0", "partner/1", "verified/1,premium/1", "" }) Out.AppendLine("  Roles " + Dump(Call(T("TwitchChat"), "Roles", b)));
        var wr = new StreamWriter(Stream.Null);
        string[] irc =
        {
            "PING :tmi.twitch.tv",
            "@badge-info=subscriber/8;badges=subscriber/6,premium/1;color=#1E90FF;display-name=Laura;emotes=25:6-10;id=a1;login=laura;msg-id=resub;msg-param-cumulative-months=8;msg-param-sub-plan=1000;room-id=1;system-msg=Laura\\ssubscribed;user-id=11 :tmi.twitch.tv USERNOTICE #canal :Hola! Kappa KEKW qué bien",
            "@badges=;color=;display-name=Pepe;emotes=;id=a2;login=pepe;msg-id=sub;msg-param-sub-plan=Prime;room-id=1;system-msg=x;user-id=12 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Pepe2;id=a2b;login=pepe2;msg-id=sub;msg-param-sub-plan=3000;user-id=12 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=vip/1;display-name=Ana;id=a3;login=ana;msg-id=submysterygift;msg-param-mass-gift-count=5;msg-param-sub-plan=1000;room-id=1;system-msg=x;user-id=13 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Ana;id=a3b;login=ana;msg-id=submysterygift;msg-param-mass-gift-count=1;user-id=13 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Ana;id=a4;login=ana;msg-id=subgift;msg-param-community-gift-id=999;msg-param-recipient-display-name=Luis;room-id=1;system-msg=x;user-id=13 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=AnAnonymousGifter;id=a5;login=ananonymousgifter;msg-id=subgift;msg-param-recipient-display-name=Marta;room-id=1;system-msg=x;user-id=274598607 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=broadcaster/1;display-name=OtroStreamer;id=a6;login=otrostreamer;msg-id=raid;msg-param-displayName=OtroStreamer;msg-param-viewerCount=320;room-id=1;system-msg=x;user-id=14 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=X;id=a6b;login=x;msg-id=raid;msg-param-viewerCount=5;user-id=14 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Y;id=a6c;login=y;msg-id=primepaidupgrade;user-id=15 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Z;id=a6d;login=z;msg-id=announcement;emotes=;user-id=15 :tmi.twitch.tv USERNOTICE #canal :Anuncio importante Pog",
            "@badges=;display-name=W;id=a6e;login=w;msg-id=bitsbadgetier;system-msg=W\\sdesbloqueó\\sinsignia;user-id=15 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=V;id=a6f;login=v;msg-id=desconocido;user-id=15 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=moderator/1;bits=500;color=#00FF7F;display-name=Mod1;emotes=;id=a7;user-id=15 :mod1!mod1@mod1.tmi.twitch.tv PRIVMSG #canal :Cheer500 vamos!",
            "@badges=;bits=0;display-name=;emotes=;id=a7b;user-id=15 :sinnombre!x@x.tmi.twitch.tv PRIVMSG #canal :bits cero",
            "@badges=;color=#000000;display-name=Oscuro;emotes=1902:0-4,9-13;id=a8;user-id=16 :oscuro!oscuro@oscuro.tmi.twitch.tv PRIVMSG #canal :Keepo y😀 Keepo fin KEKW",
            "@badges=;display-name=Act;emotes=;id=a9;user-id=17 :act!act@act.tmi.twitch.tv PRIVMSG #canal :\u0001ACTION baila\u0001",
            "@badges=;display-name=Mal;emotes=25:99-120/abc;id=a10;user-id=18 :mal!m@m PRIVMSG #canal :texto corto",
            "@login=x;target-msg-id=abc-123 :tmi.twitch.tv CLEARMSG #canal :borrado",
            "@target-user-id=999 :tmi.twitch.tv CLEARCHAT #canal :baneado",
            ":tmi.twitch.tv CLEARCHAT #canal",
            "@msg-id=msg_channel_suspended :tmi.twitch.tv NOTICE #canal :This channel has been suspended.",
            "@msg-id=otro :tmi.twitch.tv NOTICE #canal :otro aviso",
        };
        foreach (var l in irc) { Out.AppendLine(" > " + (l.Length > 60 ? l.Substring(0, 60) : l)); Call(tw, "Handle", l, wr); }
        try { Call(tw, "Handle", "RECONNECT", wr); } catch { }

        Section("KickChat");
        var kc = Make("KickChat", hub, "canal");
        string[] kick =
        {
            "{\"event\":\"App\\\\Events\\\\ChatMessageEvent\",\"data\":\"{\\\"id\\\":\\\"m1\\\",\\\"content\\\":\\\"hola [emote:37226:KEKW] y KEKW Pog [emote:1:x]\\\",\\\"sender\\\":{\\\"id\\\":7,\\\"username\\\":\\\"fan\\\",\\\"identity\\\":{\\\"color\\\":\\\"#FF0000\\\",\\\"badges\\\":[{\\\"type\\\":\\\"moderator\\\"},{\\\"type\\\":\\\"og\\\"},{\\\"type\\\":\\\"subscriber\\\",\\\"count\\\":3},{\\\"type\\\":\\\"broadcaster\\\"},{\\\"type\\\":\\\"vip\\\"},{\\\"type\\\":\\\"founder\\\"},{\\\"type\\\":\\\"verified\\\"},{\\\"type\\\":\\\"raro\\\"}]}}}\"}",
            "{\"event\":\"App\\\\Events\\\\ChatMessageEvent\",\"data\":{\"id\":\"m2\",\"content\":\"\",\"sender\":{\"username\":\"vacío\"}}}",
            "{\"event\":\"App\\\\Events\\\\MessageDeletedEvent\",\"data\":{\"message\":{\"id\":\"m1\"}}}",
            "{\"event\":\"App\\\\Events\\\\UserBannedEvent\",\"data\":{\"user\":{\"id\":7,\"username\":\"fan\"}}}",
            "{\"event\":\"App\\\\Events\\\\SubscriptionEvent\",\"data\":{\"username\":\"sub1\",\"months\":1}}",
            "{\"event\":\"App\\\\Events\\\\SubscriptionEvent\",\"data\":{\"username\":\"sub2\",\"months\":6}}",
            "{\"event\":\"App\\\\Events\\\\GiftedSubscriptionsEvent\",\"data\":{\"gifter_username\":\"rico\",\"gifted_usernames\":[\"a\",\"b\",\"c\"]}}",
            "{\"event\":\"App\\\\Events\\\\GiftedSubscriptionsEvent\",\"data\":{\"gifter_username\":\"\",\"gifted_usernames\":[\"a\"]}}",
            "{\"event\":\"App\\\\Events\\\\StreamHostEvent\",\"data\":{\"host_username\":\"amigo\",\"number_viewers\":42}}",
            "{\"event\":\"App\\\\Events\\\\FollowersUpdated\",\"data\":{\"username\":\"nuevo\",\"followed\":true}}",
            "{\"event\":\"App\\\\Events\\\\FollowersUpdated\",\"data\":{\"username\":\"adios\",\"followed\":false}}",
            "{\"event\":\"App\\\\Events\\\\KicksGifted\",\"data\":{\"sender\":{\"username\":\"generoso\"},\"gift\":{\"amount\":100}}}",
            "{\"event\":\"App\\\\Events\\\\GiftOtro\",\"data\":{\"sender\":{\"username\":\"\"},\"gift\":{\"amount\":1}}}",
            "{\"event\":\"pusher:error\",\"data\":{\"message\":\"fallo\"}}",
            "{\"event\":\"pusher_internal:subscription_succeeded\",\"data\":{}}",
            "{\"event\":\"desconocido\",\"data\":{}}",
            "no es json",
        };
        foreach (var k in kick) { Out.AppendLine(" > " + (k.Length > 60 ? k.Substring(0, 60) : k)); Call(kc, "Handle", k); }

        Section("YouTubeChat");
        var yt = Make("YouTubeChat", hub, "@canal");
        foreach (var inp in new[] { "@LofiGirl", "https://www.youtube.com/watch?v=4t4AQ15yj1M", "https://youtu.be/4t4AQ15yj1M", "4t4AQ15yj1M", "UCSJ4gkVC6NrvII8umztf0Ow", "https://www.youtube.com/c/Canal", "https://www.youtube.com/@a.b-c/live", "canal" })
            Out.AppendLine("  PageUrl(" + inp + ") = " + Call(Make("YouTubeChat", hub, inp), "PageUrl"));
        foreach (var v in new long[] { 0, 4280391411, 4294953512, 1 }) Out.AppendLine("  Argb " + Dump(Call(T("YouTubeChat"), "Argb", v)));
        string[] ytActs =
        {
            "{\"addChatItemAction\":{\"item\":{\"liveChatTextMessageRenderer\":{\"id\":\"y1\",\"authorExternalChannelId\":\"UC1\",\"authorName\":{\"simpleText\":\"@Pepa\"},\"authorBadges\":[{\"liveChatAuthorBadgeRenderer\":{\"icon\":{\"iconType\":\"MODERATOR\"}}},{\"liveChatAuthorBadgeRenderer\":{\"customThumbnail\":{}}},{\"liveChatAuthorBadgeRenderer\":{\"icon\":{\"iconType\":\"OWNER\"}}},{\"liveChatAuthorBadgeRenderer\":{\"icon\":{\"iconType\":\"VERIFIED\"}}}],\"message\":{\"runs\":[{\"text\":\"hola KEKW \"},{\"emoji\":{\"emojiId\":\"😀\",\"isCustomEmoji\":false}},{\"emoji\":{\"emojiId\":\"UCx/abc\",\"isCustomEmoji\":true,\"shortcuts\":[\":gato:\"],\"image\":{\"thumbnails\":[{\"url\":\"s\"},{\"url\":\"grande\"}]}}}]}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatTextMessageRenderer\":{\"id\":\"y1b\",\"authorName\":{\"simpleText\":\"SinTexto\"}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatPaidMessageRenderer\":{\"id\":\"y2\",\"authorName\":{\"simpleText\":\"Rico\"},\"purchaseAmountText\":{\"simpleText\":\"5,00 €\"},\"bodyBackgroundColor\":4280391411,\"message\":{\"runs\":[{\"text\":\"gracias\"}]}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatPaidMessageRenderer\":{\"id\":\"y2b\",\"authorName\":{\"simpleText\":\"Rico2\"},\"purchaseAmountText\":{\"simpleText\":\"$2.00\"}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatPaidStickerRenderer\":{\"id\":\"y3\",\"authorName\":{\"simpleText\":\"Pega\"},\"purchaseAmountText\":{\"simpleText\":\"1,99 €\"}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatMembershipItemRenderer\":{\"id\":\"y4\",\"authorName\":{\"simpleText\":\"Miembro\"},\"headerSubtext\":{\"runs\":[{\"text\":\"Te damos la \"},{\"text\":\"bienvenida\"}]}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatMembershipItemRenderer\":{\"id\":\"y4b\",\"authorName\":{\"simpleText\":\"Veterano\"},\"headerPrimaryText\":{\"runs\":[{\"text\":\"Miembro durante 5 meses\"}]},\"message\":{\"runs\":[{\"text\":\"hola\"}]}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatMembershipItemRenderer\":{\"id\":\"y4c\",\"authorName\":{\"simpleText\":\"Nadie\"}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatSponsorshipsGiftPurchaseAnnouncementRenderer\":{\"id\":\"y5\",\"header\":{\"liveChatSponsorshipsHeaderRenderer\":{\"authorName\":{\"simpleText\":\"Gen\"},\"primaryText\":{\"runs\":[{\"text\":\"ha regalado 5 membresías\"}]}}}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatViewerEngagementMessageRenderer\":{}}}}",
            "{\"markChatItemAsDeletedAction\":{\"targetItemId\":\"y1\"}}",
            "{\"markChatItemsByAuthorAsDeletedAction\":{\"externalChannelId\":\"UC1\"}}",
            "{\"otraAccion\":{}}",
        };
        foreach (var s in ytActs) { Out.AppendLine(" > " + s.Substring(0, Math.Min(60, s.Length))); Call(yt, "Action", Json.DeserializeObject(s)); }

        Section("Money");
        foreach (var m in new object[] { 5, 12.5, "3", "abc", null, 1234567.891 }) foreach (var c in new[] { "EUR", "usd", "GBP", "MXN", "ARS", "BRL", "JPY", "", null })
            Out.AppendLine("  " + Dump(m) + " " + Dump(c) + " → " + Dump(Call(T("WsClient"), "Money", m, c)));

        Section("Avisos");
        var tf = Make("TwitchFollows", hub, null, "canal");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"notification\",\"subscription_type\":\"channel.follow\"},\"payload\":{\"event\":{\"user_id\":\"123\",\"user_login\":\"nuevo_seguidor\",\"user_name\":\"Nuevo_Seguidor\"}}}");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"notification\",\"subscription_type\":\"channel.follow\"},\"payload\":{\"event\":{\"user_id\":\"124\",\"user_login\":\"solo_login\",\"user_name\":\"\"}}}");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"notification\",\"subscription_type\":\"otra\"},\"payload\":{}}");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"revocation\"}}");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"session_keepalive\"}}");
        var se = Make("StreamElementsAlerts", hub, " tok ");
        foreach (var s in new[] {
            "{\"type\":\"message\",\"topic\":\"channel.activities\",\"data\":{\"type\":\"tip\",\"provider\":\"twitch\",\"data\":{\"username\":\"donante\",\"displayName\":\"Donante\",\"amount\":5,\"currency\":\"EUR\",\"message\":\"Ánimo KEKW\"}}}",
            "{\"type\":\"message\",\"topic\":\"channel.activities\",\"data\":{\"type\":\"tip\",\"data\":{\"amount\":\"7.5\",\"currency\":\"USD\"}}}",
            "{\"type\":\"message\",\"topic\":\"channel.activities\",\"data\":{\"type\":\"follower\",\"provider\":\"twitch\",\"data\":{\"username\":\"fan2\"}}}",
            "{\"type\":\"message\",\"topic\":\"channel.activities\",\"data\":{\"type\":\"follower\",\"provider\":\"youtube\",\"data\":{\"username\":\"fan3\"}}}",
            "{\"type\":\"message\",\"topic\":\"otro\",\"data\":{}}",
            "{\"type\":\"response\",\"error\":\"err_bad_request\"}",
            "{\"type\":\"response\"}",
            "{\"type\":\"response\",\"error\":\"err_unauthorized\"}" }) Call(se, "OnText", s);
        var sl = Make("StreamlabsAlerts", hub, "tok");
        foreach (var s in new[] {
            "0{\"sid\":\"x\",\"pingInterval\":10000}", "0{\"pingInterval\":999999}", "40", "3",
            "42[\"event\",{\"type\":\"donation\",\"message\":[{\"name\":\"Pablo\",\"amount\":\"12.5\",\"formatted_amount\":\"12,50 €\",\"currency\":\"EUR\",\"message\":\"Para un café Pog\"},{\"name\":\"\",\"amount\":\"3\",\"currency\":\"USD\",\"message\":\"\"}],\"for\":\"streamlabs\"}]",
            "42[\"event\",{\"type\":\"follow\",\"message\":[{\"name\":\"segui\"}],\"for\":\"twitch_account\"}]",
            "42[\"event\",{\"type\":\"follow\",\"message\":[{\"name\":\"yt\"}],\"for\":\"youtube_account\"}]",
            "42[\"otro\",{}]", "44\"Authentication error\"" }) Call(sl, "OnText", s);
        Out.AppendLine("  PingMs=" + sl.GetType().BaseType.GetField("PingMs", F).GetValue(sl));

        Console.OutputEncoding = Encoding.UTF8;
        Console.Write(Out.ToString());
        return 0;
    }

    static void OnItem<T>(T it) { Out.AppendLine("  item " + Dump(it)); }
}
