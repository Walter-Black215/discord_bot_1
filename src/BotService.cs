using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordAnalyzer
{
    // Ошибка, после которой переподключаться бесполезно (например, неверный токен)
    public class FatalGatewayException : Exception
    {
        public FatalGatewayException(string message) : base(message) { }
    }

    // Бот с командой /анализ: держит соединение с Discord и отвечает на команду отчётом
    public class CommandBot
    {
        readonly string token;
        readonly DiscordClient api;
        readonly Action<string> log;
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        readonly object runLock = new object();
        readonly HashSet<string> running = new HashSet<string>();
        readonly Dictionary<string, DateTime> lastRun = new Dictionary<string, DateTime>();

        CancellationTokenSource cts;
        ClientWebSocket ws;
        long? seq;
        volatile bool acked = true;
        volatile bool isRunning;
        string appId;

        const int CooldownSeconds = 60;
        const ulong ManageGuild = 1UL << 5;
        const ulong Administrator = 1UL << 3;

        public event Action Stopped;
        public string BotName;
        public bool IsRunning { get { return isRunning; } }
        public string AppId { get { return appId; } }

        public CommandBot(string botToken, Action<string> logger)
        {
            token = botToken;
            api = new DiscordClient(botToken);
            log = logger;
        }

        // Ссылка, по которой админы добавляют бота на свой сервер
        public static string InviteUrl(string applicationId)
        {
            if (string.IsNullOrEmpty(applicationId)) return null;
            return "https://discord.com/oauth2/authorize?client_id=" + applicationId + "&scope=bot%20applications.commands&permissions=66592";
        }

        public async Task Start()
        {
            cts = new CancellationTokenSource();
            await Task.Run(() => Prepare());
            isRunning = true;
            var loop = Loop(cts.Token);
        }

        public void Stop()
        {
            try { if (cts != null) cts.Cancel(); } catch { }
            try { if (ws != null) ws.Abort(); } catch { }
        }

        // Узнаём ID приложения и регистрируем команду /анализ (/analyze)
        void Prepare()
        {
            var me = Json.Obj(api.Get("/users/@me"));
            BotName = Json.Str(me, "username");
            try { appId = Json.Str(Json.Obj(api.Get("/oauth2/applications/@me")), "id"); } catch (ApiException) { }
            if (string.IsNullOrEmpty(appId)) appId = Json.Str(me, "id");

            var cmd = new Dictionary<string, object>
            {
                { "name", "analyze" },
                { "name_localizations", new Dictionary<string, object> { { "ru", "анализ" } } },
                { "description", "Server analysis: stats, pros and cons, growth tips" },
                { "description_localizations", new Dictionary<string, object> { { "ru", "Разбор сервера: статистика, плюсы и минусы, советы по продвижению" } } },
                { "type", 1 },
                { "default_member_permissions", "32" },
                { "contexts", new object[] { 0 } },
                { "integration_types", new object[] { 0 } }
            };
            api.Send("PUT", "/applications/" + appId + "/commands", new object[] { cmd });
            log("Команда /анализ зарегистрирована у бота «" + BotName + "».");
        }

        async Task Loop(CancellationToken ct)
        {
            int delay = 2;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RunSession(ct);
                }
                catch (FatalGatewayException ex)
                {
                    log("Ошибка: " + ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    log("Связь с Discord прервалась (" + ex.Message + "). Переподключаюсь через " + delay + " с…");
                }
                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(delay * 1000, ct); } catch { break; }
                delay = Math.Min(60, delay * 2);
            }
            isRunning = false;
            log("Бот остановлен.");
            var h = Stopped;
            if (h != null) { try { h(); } catch { } }
        }

        async Task RunSession(CancellationToken ct)
        {
            var gw = Json.Obj(await Task.Run(() => api.Get("/gateway/bot")));
            string url = (Json.Str(gw, "url") ?? "wss://gateway.discord.gg") + "/?v=10&encoding=json";
            using (var socket = new ClientWebSocket())
            {
                ws = socket;
                await socket.ConnectAsync(new Uri(url), ct);
                var hello = Json.Obj(await Receive(ct));
                if (Json.Long(hello, "op", -1) != 10) throw new Exception("Discord не прислал приветствие");
                int interval = (int)Json.Long(Json.Obj(Json.Get(hello, "d")), "heartbeat_interval", 41250);
                acked = true;
                using (var hbCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var hb = Heartbeat(interval, hbCts.Token);
                    var identify = new Dictionary<string, object>
                    {
                        { "op", 2 },
                        { "d", new Dictionary<string, object>
                            {
                                { "token", token },
                                { "intents", 1 },
                                { "properties", new Dictionary<string, object> { { "os", "windows" }, { "browser", "discord-analyzer" }, { "device", "discord-analyzer" } } }
                            }
                        }
                    };
                    await SendJson(identify, ct);
                    try
                    {
                        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                        {
                            var msg = Json.Obj(await Receive(ct));
                            if (msg == null) continue;
                            long op = Json.Long(msg, "op", -1);
                            object s = Json.Get(msg, "s");
                            if (s != null) seq = Convert.ToInt64(s);
                            if (op == 0) Dispatch(Json.Str(msg, "t"), Json.Obj(Json.Get(msg, "d")));
                            else if (op == 11) acked = true;
                            else if (op == 1) await SendHeartbeat(ct);
                            else if (op == 7) throw new Exception("Discord попросил переподключиться");
                            else if (op == 9) { seq = null; throw new Exception("сессия устарела"); }
                        }
                    }
                    finally
                    {
                        hbCts.Cancel();
                    }
                }
            }
        }

        async Task<object> Receive(CancellationToken ct)
        {
            var buf = new ArraySegment<byte>(new byte[16384]);
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    var res = await ws.ReceiveAsync(buf, ct);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        int code = ws.CloseStatus.HasValue ? (int)ws.CloseStatus.Value : 0;
                        if (code == 4004) throw new FatalGatewayException("токен бота не подходит. Получите новый: Developer Portal → Бот → «Сбросить токен».");
                        if (code == 4013 || code == 4014) throw new FatalGatewayException("Discord не разрешил подключение (код " + code + ").");
                        throw new Exception("Discord закрыл соединение, код " + code);
                    }
                    ms.Write(buf.Array, 0, res.Count);
                    if (res.EndOfMessage) break;
                }
                return Json.Parse(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }

        async Task Heartbeat(int interval, CancellationToken ct)
        {
            try
            {
                await Task.Delay((int)(interval * new Random().NextDouble()), ct);
                while (!ct.IsCancellationRequested)
                {
                    if (!acked)
                    {
                        // Discord не ответил на прошлый сигнал — соединение «зависло», переподключаемся
                        try { ws.Abort(); } catch { }
                        return;
                    }
                    acked = false;
                    await SendHeartbeat(ct);
                    await Task.Delay(interval, ct);
                }
            }
            catch { }
        }

        Task SendHeartbeat(CancellationToken ct)
        {
            return SendJson(new Dictionary<string, object> { { "op", 1 }, { "d", seq.HasValue ? (object)seq.Value : null } }, ct);
        }

        async Task SendJson(object o, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(Json.Serialize(o));
            await sendLock.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            finally { sendLock.Release(); }
        }

        void Dispatch(string type, Dictionary<string, object> d)
        {
            if (type == "READY")
            {
                var u = Json.Obj(Json.Get(d, "user"));
                int guilds = Json.Arr(Json.Get(d, "guilds")).Length;
                log("Бот «" + Json.Str(u, "username") + "» в сети. Серверов, где он добавлен: " + guilds + ".");
            }
            else if (type == "INTERACTION_CREATE")
            {
                var data = d;
                Task.Run(() => HandleInteraction(data));
            }
        }

        // ---------- Команда /анализ ----------

        void HandleInteraction(Dictionary<string, object> d)
        {
            if (Json.Long(d, "type", 0) != 2) return;
            var data = Json.Obj(Json.Get(d, "data"));
            if (Json.Str(data, "name") != "analyze") return;

            string id = Json.Str(d, "id");
            string itoken = Json.Str(d, "token");
            string guildId = Json.Str(d, "guild_id");
            var member = Json.Obj(Json.Get(d, "member"));
            var user = Json.Obj(Json.Get(member, "user"));
            string who = Json.Str(user, "global_name") ?? Json.Str(user, "username") ?? "?";

            // Сразу говорим Discord «думаю…» (иначе через 3 секунды команда считается не отвеченной). Флаг 64 — ответ видит только автор.
            try
            {
                api.Send("POST", "/interactions/" + id + "/" + itoken + "/callback",
                    new Dictionary<string, object> { { "type", 5 }, { "data", new Dictionary<string, object> { { "flags", 64 } } } });
            }
            catch (Exception ex)
            {
                log("Не успел ответить на команду: " + ex.Message);
                return;
            }

            string edit = "/webhooks/" + appId + "/" + itoken + "/messages/@original";
            if (guildId == null)
            {
                Reply(edit, "Команда работает только на сервере.");
                return;
            }

            ulong perms;
            ulong.TryParse(Json.Str(member, "permissions"), out perms);
            if ((perms & ManageGuild) == 0 && (perms & Administrator) == 0)
            {
                Reply(edit, "Команда доступна только тем, у кого есть право «Управлять сервером».");
                return;
            }

            string busyText = null;
            lock (runLock)
            {
                DateTime last;
                if (running.Contains(guildId)) busyText = "Анализ этого сервера уже идёт. Подождите немного.";
                else if (lastRun.TryGetValue(guildId, out last) && (DateTime.Now - last).TotalSeconds < CooldownSeconds)
                    busyText = "Сервер недавно проверяли. Повторить можно через " + (int)Math.Ceiling(CooldownSeconds - (DateTime.Now - last).TotalSeconds) + " с.";
                else running.Add(guildId);
            }
            if (busyText != null)
            {
                Reply(edit, busyText);
                return;
            }

            log("/анализ: сервер " + guildId + ", вызвал(а) " + who + "…");
            try
            {
                var r = Analyzer.AnalyzeBot(api, guildId, s => { }, true);
                byte[] file = Encoding.UTF8.GetBytes(HtmlReport.Build(r));
                api.SendWithFile("PATCH", edit, BuildPayload(r), "analysis-report.html", file);
                log("Отчёт отправлен: «" + r.Name + "», оценка " + r.Score + "/100.");
            }
            catch (Exception ex)
            {
                var a = ex as ApiException;
                string msg = a != null && a.Status == 403
                    ? "Боту не хватает прав. Нужны: «Просматривать каналы» и «Читать историю сообщений»."
                    : "Не получилось сделать анализ: " + ex.Message;
                Reply(edit, msg);
                log("Ошибка анализа (" + guildId + "): " + ex.Message);
            }
            finally
            {
                lock (runLock)
                {
                    running.Remove(guildId);
                    lastRun[guildId] = DateTime.Now;
                }
            }
        }

        void Reply(string path, string text)
        {
            try { api.Send("PATCH", path, new Dictionary<string, object> { { "content", text }, { "allowed_mentions", new Dictionary<string, object> { { "parse", new object[0] } } } }); }
            catch (Exception ex) { log("Не удалось ответить: " + ex.Message); }
        }

        // ---------- Сообщение с отчётом ----------

        static string Cut(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        static string Bullets(IEnumerable<string> items, int max)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var it in items)
            {
                if (n >= max) break;
                string line = "• " + Cut(it, 230) + "\n";
                if (sb.Length + line.Length > 1000) break;
                sb.Append(line);
                n++;
            }
            string res = sb.ToString().TrimEnd();
            return res.Length == 0 ? "—" : res;
        }

        static Dictionary<string, object> Field(string name, string value)
        {
            return new Dictionary<string, object> { { "name", name }, { "value", value }, { "inline", false } };
        }

        public static Dictionary<string, object> BuildPayload(Report r)
        {
            int color = r.Score >= 65 ? 0x5BBF7A : r.Score >= 50 ? 0xE8B931 : 0xE05A4F;
            var sb = new StringBuilder();
            sb.Append("Оценка: **" + r.Score + " из 100** — " + r.Grade + "\n\n");
            string[] keys = { "Участников", "В сети сейчас", "Возраст сервера", "Новые за 30 дней", "Пишут за неделю", "Живые каналы", "Бусты", "С прошлой проверки" };
            foreach (var k in keys)
            {
                var st = r.Stats.FirstOrDefault(x => x[0] == k);
                if (st != null) sb.Append("**" + k + ":** " + st[1] + "\n");
            }

            var first = r.Tips.Where(x => x.Prio <= 1).OrderBy(x => x.Prio).Select(x => x.Text).ToList();
            if (first.Count < 4) first.AddRange(r.Tips.Where(x => x.Prio == 2).Select(x => x.Text).Take(4 - first.Count));

            var fields = new List<object>
            {
                Field("✅ Плюсы (" + r.Pluses.Count + ")", Bullets(r.Pluses.OrderBy(x => x.Prio).Select(x => x.Text), 5)),
                Field("⚠️ Минусы (" + r.Minuses.Count + ")", Bullets(r.Minuses.OrderBy(x => x.Prio).Select(x => x.Text), 6)),
                Field("🛠 Что сделать в первую очередь", Bullets(first, 5))
            };

            var embed = new Dictionary<string, object>
            {
                { "title", Cut("Анализ сервера: " + r.Name, 250) },
                { "description", Cut(sb.ToString(), 3500) },
                { "color", color },
                { "fields", fields },
                { "footer", new Dictionary<string, object> { { "text", "Полный отчёт с таблицами — в файле ниже, откройте его в браузере. Советы основаны на анализе 38 серверов Rust-кланов." } } }
            };
            if (!string.IsNullOrEmpty(r.IconUrl)) embed["thumbnail"] = new Dictionary<string, object> { { "url", r.IconUrl } };

            return new Dictionary<string, object>
            {
                { "content", "" },
                { "allowed_mentions", new Dictionary<string, object> { { "parse", new object[0] } } },
                { "embeds", new object[] { embed } },
                { "attachments", new object[] { new Dictionary<string, object> { { "id", 0 }, { "filename", "analysis-report.html" } } } }
            };
        }
    }
}
