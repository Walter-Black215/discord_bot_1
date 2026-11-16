using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscordAnalyzer
{
    public class Finding
    {
        public string Text;
        public int Prio; // 0 — срочно, 1 — важно, 2 — желательно, 3 — общее
        public Finding(string text, int prio) { Text = text; Prio = prio; }
    }

    public class ChannelInfo
    {
        public string Id, Name, Norm, ParentId, ParentName, ParentNorm, Status;
        public int Type, Position;
        public DateTime? Last;
        public bool IsInfo, IsPartner;
    }

    public class Report
    {
        public string Mode, Name, Id, Description, IconUrl, InviteCode, Vanity;
        public long Members = -1, Online = -1;
        public DateTime Created = DateTime.MinValue;
        public DateTime Generated = DateTime.Now;
        public List<string[]> Stats = new List<string[]>();
        public List<Finding> Pluses = new List<Finding>();
        public List<Finding> Minuses = new List<Finding>();
        public List<Finding> Tips = new List<Finding>();
        public List<string> Notes = new List<string>();

        // Только для полного разбора через бота
        public List<ChannelInfo> Channels;
        public List<string[]> KeyChecks = new List<string[]>();   // что, найдено, зачем
        public List<string[]> RoleRows;                           // роль, людей, заметка
        public List<string> BotNames;
        public List<string[]> BotChecks = new List<string[]>();   // зачем, найдено, что поставить
        public int[] JoinWeeks;
        public List<string[]> InviteRows;                         // код, канал, автор, переходов, срок

        public double Earned, Max;

        public int Score { get { return Max <= 0 ? 0 : (int)Math.Round(100.0 * Earned / Max); } }

        public string Grade
        {
            get
            {
                int s = Score;
                if (s >= 80) return "Отлично";
                if (s >= 65) return "Хорошо";
                if (s >= 50) return "Средне";
                if (s >= 35) return "Слабо";
                return "Плохо";
            }
        }

        public void Crit(double weight, double value)
        {
            Max += weight;
            Earned += weight * Math.Max(0, Math.Min(1, value));
        }

        public void Stat(string name, string value, string note) { Stats.Add(new[] { name, value, note ?? "" }); }
        public void Plus(string t, int p) { Pluses.Add(new Finding(t, p)); }
        public void Minus(string t, int p) { Minuses.Add(new Finding(t, p)); }
        public void Tip(string t, int p) { if (!Tips.Any(x => x.Text == t)) Tips.Add(new Finding(t, p)); }
    }

    public class ConnectResult
    {
        public string BotName, BotId;
        public List<GuildItem> Guilds = new List<GuildItem>();
    }

    public class GuildItem
    {
        public string Id, Name;
        public override string ToString() { return Name; }
    }

    public static class Analyzer
    {
        const RegexOptions RO = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        const string WelcomeRx = "welcom|добро|привет|rule|правил|start|info|инфо|navigation|навигац|guide|new|новичк|hello|lobby";
        const string InfoRx = "rule|правил|info|инфо|welcom|добро|привет|требован|requirement|price|прайс|ticket|тикет|заявк|link|ссылк|guide|навигац|navigation|role|роли|faq|recruit|набор|black|блэк|ч[её]рн|partner|партн|ads|реклам|stat|countdown|boost|буст|apply|server|сервер|site|сайт|news|новост|announce|объявл";
        const string PartnerCatRx = "partner|партн|ads|реклам|сотруднич|parthner|advert|sponsor|спонсор";
        const string StaffRx = "owner|овнер|владел|founder|leader|лидер|admin|админ|moder|модер|staff|стафф|ticket|тикет|helper|хелпер|support|саппорт|manager|менеджер|safety";
        const string ExRx = "(^|[^a-z])ex([^a-z]|$)|ex-|бывш|history|истори|old|олд|ветеран";
        const string MediaRx = "vip|вип|media|медиа|youtube|ютуб|stream|стрим|big ?name|famous|fame|creator|tiktok|swag|hot";

        class KeyDef
        {
            public string Title, Rx, Why, Exclude;
            public double W;
            public bool Voice;
            public KeyDef(string t, string rx, string why, double w, bool voice) { Title = t; Rx = rx; Why = why; W = w; Voice = voice; }
        }

        static readonly KeyDef[] Keys =
        {
            new KeyDef("Приветствие", "welcom|добро|привет|start|navigation|навигац|guide|new-?(people|member|comer)|новичк|hello|lobby", "Первое, что видит новичок: кто вы и куда идти.", 1, false),
            new KeyDef("Правила", "rule|правил", "Без правил трудно наказывать нарушителей. Есть у большинства изученных серверов.", 1, false),
            new KeyDef("Требования для вступления", "требован|requirement|ticket-?info|info-?ticket|тикет-?инфо|recruit|набор|критери", "Кандидат заранее видит условия: часы, возраст, онлайн. Меньше пустых заявок.", 1, false),
            new KeyDef("Заявки (тикеты)", "ticket|тикет|заявк|apply|application|вступ|join", "Главная воронка набора: кнопка → личный канал с анкетой.", 1, false) { Exclude = "info|инфо|требован|requirement|критери" },
            new KeyDef("Голосовые для проверки", "check|провер|обзвон|waiting|ожидан|прихож|reception|при[её]м|calling|tryout|verif", "Проверка кандидатов голосом есть у 22 из 34 изученных серверов.", 0.5, true),
            new KeyDef("Новости", "news|новост|announce|объявл|анонс", "Сюда идут итоги, набор и важные события с @everyone.", 1, false),
            new KeyDef("Прогресс вайпа / победы", "wipe|вайп|progress|прогрес|raid|рейд|achiev|достиж|result|результ|wins|побед|tourn|турнир|recap|memor", "Показ силы клана — лучшая реклама.", 1, false),
            new KeyDef("Медиа и видео", "media|медиа|tiktok|тикток|youtube|ютуб|stream|стрим|clip|клип|video|видео|видос|moment|момент|movie|shorts", "Видео — главный источник новых людей у лидеров.", 1, false),
            new KeyDef("Розыгрыши и ивенты", "giveaway|розыгр|раздач|event|ивент|конкурс|приз|draw|gift|nitro", "Розыгрыш за приглашения растит сервер.", 0.5, false),
            new KeyDef("Учёт приглашений", "invite|инвайт|приглаш", "Видно, кто и сколько людей привёл (для розыгрышей).", 0.5, false),
            new KeyDef("Реклама (прайс)", "ads|реклам|price|прайс|advert", "Продажа рекламы — главный доход кланов.", 0.5, false),
            new KeyDef("Общий чат", "chat|чат|общени|general|global|флуд|talk|болталк", "Место, где живёт сообщество.", 1, false),
            new KeyDef("Чат для Европы (EU)", "(^|[^a-z])eu([^a-z]|$)|english|(^|[^a-z])eng([^a-z]|$)|(^|[^a-z])en-|international", "У всех лидеров два языка — это вдвое больше аудитории.", 0.5, false),
            new KeyDef("Поиск напарников (LFG)", "lfg|lfc|lft|поиск|looking|find|search|тиммейт|teammate|ищу", "Новички находят команду и остаются на сервере.", 0.5, false),
            new KeyDef("Самореклама участников", "self-?promo|селф|promo|ваши-?(видос|клип|мув)|your-?(media|clip|video)", "Участники приносят свои видео и зрителей.", 0.5, false),
            new KeyDef("Чёрный список", "black|блэк|ч[её]рн|shame|позор|ban-?list|бан-?лист", "Защита от читеров и «крыс», доверие к клану.", 0.5, false),
            new KeyDef("Развлечения (мемы и т.п.)", "meme|мем|похваст|fun|юмор|сетап|setup|offtop|оффтоп", "Держат зрителей, которые не попали в клан (пример SOSO GANG).", 0.5, false),
            new KeyDef("Спасибо за буст", "boost|буст", "Благодарность бустерам мотивирует бустить сервер.", 0.5, false),
        };

        class BotDef
        {
            public string Title, Rx, Rec;
            public BotDef(string t, string rx, string rec) { Title = t; Rx = rx; Rec = rec; }
        }

        static readonly BotDef[] BotDefs =
        {
            new BotDef("Заявки (тикеты)", "ticket|tickety", "Ticket Tool — стоит на 18 из 34 изученных серверов"),
            new BotDef("Учёт приглашений", "invite", "Invite Tracker или InviteLogger"),
            new BotDef("Счётчик участников", "stat|count|member", "ServerStats"),
            new BotDef("Модерация и роли", "carl|juniper|multibot|mee6|dyno|probot|sapphire|yagpdb|draftbot|arcane", "Carl-bot, JuniperBot или MultiBot"),
            new BotDef("Розыгрыши", "giveaway|prize", "GiveawayBot"),
            new BotDef("Защита сервера", "security|wick|antievil|anti|beemo|guard|protect", "Security или Wick"),
        };

        static readonly KeyValuePair<ulong, string>[] DangerPerms =
        {
            new KeyValuePair<ulong, string>(1UL << 3, "Администратор"),
            new KeyValuePair<ulong, string>(1UL << 5, "Управлять сервером"),
            new KeyValuePair<ulong, string>(1UL << 28, "Управлять ролями"),
            new KeyValuePair<ulong, string>(1UL << 4, "Управлять каналами"),
            new KeyValuePair<ulong, string>(1UL << 2, "Банить"),
            new KeyValuePair<ulong, string>(1UL << 1, "Выгонять"),
            new KeyValuePair<ulong, string>(1UL << 13, "Управлять сообщениями"),
            new KeyValuePair<ulong, string>(1UL << 17, "Упоминать @everyone"),
            new KeyValuePair<ulong, string>(1UL << 29, "Управлять вебхуками"),
        };

        // ---------- Разбор ссылки ----------

        public static string ParseInvite(string input)
        {
            if (input == null) return null;
            string s = input.Trim();
            int q = s.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) s = s.Substring(0, q);
            s = s.TrimEnd('/');
            int slash = s.LastIndexOf('/');
            if (slash >= 0) s = s.Substring(slash + 1);
            if (!Regex.IsMatch(s, "^[A-Za-z0-9-]{2,64}$")) return null;
            return s;
        }

        // ---------- Режим 1: по ссылке-приглашению ----------

        public static Report AnalyzeInvite(string input, Action<string> progress, bool useHistory)
        {
            string code = ParseInvite(input);
            if (code == null) throw new Exception("Это не похоже на ссылку-приглашение. Пример: https://discord.gg/abc123");
            var api = new DiscordClient(null);
            progress("Запрашиваю данные по приглашению…");
            var inv = Json.Obj(api.Get("/invites/" + Uri.EscapeDataString(code) + "?with_counts=true&with_expiration=true"));
            var g = Json.Obj(Json.Get(inv, "guild"));
            if (g == null) throw new Exception("Эта ссылка ведёт не на сервер (например, в групповой чат). Нужна ссылка-приглашение на сервер.");

            var r = new Report();
            r.Mode = "Анализ по ссылке-приглашению";
            r.InviteCode = code;
            Basics(r, g);
            r.Members = Json.Long(inv, "approximate_member_count", -1);
            r.Online = Json.Long(inv, "approximate_presence_count", -1);

            EvalCommon(r, g);

            string exp = Json.Str(inv, "expires_at");
            if (!string.IsNullOrEmpty(exp))
            {
                var d = Util.ParseIso(exp);
                r.Minus("Ссылка-приглашение временная" + (d.HasValue ? " (перестанет работать " + d.Value.ToString("dd.MM.yyyy HH:mm") + ")" : "") +
                        ". В рекламе такая ссылка скоро станет нерабочей.", 1);
                r.Tip("Для рекламы создайте бессрочную ссылку-приглашение (при создании ссылки выберите срок «Никогда»).", 1);
                r.Crit(0.5, 0);
            }
            else r.Crit(0.5, 1);

            var ch = Json.Obj(Json.Get(inv, "channel"));
            string chName = Json.Str(ch, "name");
            if (!string.IsNullOrEmpty(chName))
            {
                r.Stat("Ссылка ведёт в канал", "#" + chName, null);
                if (Regex.IsMatch(Util.Norm(chName), WelcomeRx, RO))
                {
                    r.Plus("Ссылка ведёт в канал «" + chName + "»: новичок сразу видит приветствие или правила.", 3);
                    r.Crit(0.5, 1);
                }
                else
                {
                    r.Tip("Ссылка ведёт в канал «" + chName + "». Лучше вести новичков в канал приветствия или правил: так они быстрее понимают, куда попали.", 2);
                    r.Crit(0.5, 0.4);
                }
            }

            try
            {
                progress("Проверяю виджет сервера…");
                var w = Json.Obj(api.Get("/guilds/" + r.Id + "/widget.json"));
                int vc = Json.Arr(Json.Get(w, "channels")).Length;
                int inVoice = Json.Arr(Json.Get(w, "members")).Count(x => !string.IsNullOrEmpty(Json.Str(Json.Obj(x), "channel_id")));
                r.Stat("Сейчас в голосовых", inVoice + " чел.", "по виджету сервера; открытых голосовых каналов: " + vc);
                if (inVoice >= 10) r.Plus("Живые голосовые каналы: прямо сейчас в голосе " + inVoice + " чел.", 2);
            }
            catch { }

            if (useHistory) HistoryCompare(r);
            GeneralTips(r, true);
            r.Notes.Add("По ссылке видны только общие данные. Каналы, роли и боты не видны. Для полного разбора своего сервера используйте режим «Мой сервер (через бота)».");
            r.Notes.Add("Число людей в сети зависит от времени суток. Честнее сравнивать вечером (19:00–23:00 по Москве).");
            return r;
        }

        // ---------- Режим 2: свой сервер через бота ----------

        public static ConnectResult Connect(DiscordClient api)
        {
            var res = new ConnectResult();
            var me = Json.Obj(api.Get("/users/@me"));
            res.BotId = Json.Str(me, "id");
            res.BotName = Json.Str(me, "username");
            foreach (var x in Json.Arr(api.Get("/users/@me/guilds")))
            {
                var d = Json.Obj(x);
                if (d == null) continue;
                res.Guilds.Add(new GuildItem { Id = Json.Str(d, "id"), Name = Json.Str(d, "name") });
            }
            return res;
        }

        public static Report AnalyzeBot(DiscordClient api, string guildId, Action<string> progress, bool useHistory)
        {
            progress("Читаю настройки сервера…");
            var g = Json.Obj(api.Get("/guilds/" + guildId + "?with_counts=true"));
            if (g == null) throw new Exception("Discord не вернул данные сервера.");

            var r = new Report();
            r.Mode = "Полный разбор через бота";
            Basics(r, g);
            r.Members = Json.Long(g, "approximate_member_count", -1);
            r.Online = Json.Long(g, "approximate_presence_count", -1);

            EvalCommon(r, g);
            EvalSettings(r, g);

            progress("Читаю каналы…");
            var chans = ParseChannels(Json.Arr(api.Get("/guilds/" + guildId + "/channels")));
            var roles = Json.Arr(Json.Get(g, "roles"));
            if (roles.Length == 0) roles = Json.Arr(api.Get("/guilds/" + guildId + "/roles"));

            List<Dictionary<string, object>> members = null;
            try { members = LoadMembers(api, guildId, progress); }
            catch (ApiException)
            {
                r.Notes.Add("Список участников недоступен. Включите для бота «Server Members Intent» на сайте Discord Developer Portal (раздел Bot). Тогда появятся прирост по неделям, число людей в ролях и участники без ролей.");
            }

            // Узнаём ID самого бота-анализатора, чтобы не считать его среди ботов сервера
            string selfId = null;
            try { selfId = Json.Str(Json.Obj(api.Get("/users/@me")), "id"); } catch (ApiException) { }

            EvalChannels(r, chans, g);
            EvalRoles(r, roles, members, guildId);
            EvalBots(r, roles, members, selfId);
            if (members != null) EvalMembers(r, members);

            progress("Читаю ссылки-приглашения…");
            try { EvalInvites(r, Json.Arr(api.Get("/guilds/" + guildId + "/invites"))); }
            catch (ApiException ex)
            {
                if (ex.Status == 403)
                    r.Notes.Add("Ссылки-приглашения не видны: у бота нет права «Управлять сервером». С этим правом программа покажет, сколько людей пришло по каждой ссылке.");
                else
                    r.Notes.Add("Не удалось прочитать ссылки-приглашения: " + ex.Message);
            }

            EvalActivity(r, api, chans, progress);

            if (useHistory) HistoryCompare(r);
            GeneralTips(r, false);
            return r;
        }

        // ---------- Общие проверки ----------

        static void Basics(Report r, Dictionary<string, object> g)
        {
            r.Id = Json.Str(g, "id");
            r.Name = Json.Str(g, "name") ?? "Без названия";
            r.Description = Json.Str(g, "description");
            string icon = Json.Str(g, "icon");
            if (!string.IsNullOrEmpty(icon))
                r.IconUrl = "https://cdn.discordapp.com/icons/" + r.Id + "/" + icon + (icon.StartsWith("a_") ? ".gif" : ".png") + "?size=128";
            r.Vanity = Json.Str(g, "vanity_url_code");
            r.Created = Util.FromSnowflake(r.Id);
        }

        static void EvalCommon(Report r, Dictionary<string, object> g)
        {
            long m = r.Members, o = r.Online;

            // Размер
            if (m >= 0)
            {
                double v; string tier;
                if (m >= 2000) { v = 1; tier = "уровень лидеров (2 000+)"; }
                else if (m >= 1000) { v = 0.8; tier = "крупный (1 000–2 000)"; }
                else if (m >= 600) { v = 0.6; tier = "средний (600–1 000)"; }
                else if (m >= 300) { v = 0.4; tier = "маленький (300–600)"; }
                else { v = 0.2; tier = "начинающий (до 300)"; }
                r.Crit(3, v);
                string myId = r.Id;
                int total = Bench.Clans.Count(c => c.Id != myId);
                int below = Bench.Clans.Count(c => c.Id != myId && c.M < m);
                int pct = (int)Math.Round(100.0 * below / Math.Max(1, total));
                r.Stat("Участников", Util.N(m), tier + "; больше, чем у " + pct + "% из " + total + " изученных клановых серверов");
                if (m >= 1000) r.Plus("Большая аудитория: " + Util.N(m) + " участников — больше, чем у " + pct + "% изученных Rust-кланов.", 2);
                else if (m < 300) r.Minus("Мало участников (" + Util.N(m) + "). У типичного клана 600–1 000, у лидеров 2 000–4 500.", 2);
            }

            // Активность
            if (m > 0 && o >= 0)
            {
                double ratio = (double)o / m;
                int p = (int)Math.Round(ratio * 100);
                if (m < 50)
                {
                    r.Stat("В сети сейчас", Util.N(o) + " (" + p + "%)", "на сервере меньше 50 участников — процент пока не показателен");
                }
                else
                {
                r.Stat("В сети сейчас", Util.N(o) + " (" + p + "%)", "у живых серверов 25–40%; у лидеров 32–42%");
                double v;
                if (ratio >= 0.35) { v = 1; r.Plus("Очень живая аудитория: в сети " + p + "% участников (у лидеров 32–42%).", 1); }
                else if (ratio >= 0.25) { v = 0.75; r.Plus("Нормальная активность: в сети " + p + "% участников (норма 25–40%).", 2); }
                else if (ratio >= 0.15)
                {
                    v = 0.4;
                    r.Minus("Низкая активность: в сети только " + p + "% участников. Много «спящих» людей.", 1);
                    r.Tip("Оживите сервер: розыгрыш раз в 2–4 недели, итоги вайпа с @everyone, живые каналы (мемы, клипы, «похвастайся»).", 1);
                }
                else
                {
                    v = 0.1;
                    r.Minus("Очень низкая активность: в сети " + p + "%. Похоже на много неактивных или ненастоящих аккаунтов.", 0);
                    r.Tip("Проверьте, нет ли накрученных участников-ботов. Важнее живая активность, чем большое число участников.", 1);
                }
                r.Crit(3, v);
                }
            }

            // Возраст и скорость роста
            if (r.Created > DateTime.MinValue)
            {
                double days = (DateTime.Now - r.Created).TotalDays;
                double months = days / 30.44;
                r.Stat("Возраст сервера", Util.Age(days), "создан " + r.Created.ToString("dd.MM.yyyy"));
                if (m > 0 && months >= 0.5)
                {
                    double per = m / months;
                    r.Stat("Средний рост", "+" + Util.N(per) + " в месяц", "у лидеров обычно +60…+220 в месяц");
                    double v = per >= 150 ? 1 : per >= 100 ? 0.85 : per >= 60 ? 0.65 : per >= 30 ? 0.4 : 0.15;
                    r.Crit(2, v);
                    if (per >= 100) r.Plus("Быстрый рост: в среднем +" + Util.N(per) + " участников в месяц (у лидеров +60…+220).", 2);
                    else if (per < 30 && months >= 3)
                    {
                        r.Minus("Медленный рост: в среднем +" + Util.N(per) + " участников в месяц.", 1);
                        r.Tip("Нужен внешний поток людей: видео после каждого вайпа, свой канал набора на большом сервере, турниры, розыгрыш за приглашения.", 1);
                    }
                }
                if (months >= 12) r.Plus("Сервер работает " + Util.Age(days) + " — это постоянство и доверие. Всем лидерам больше года.", 3);
                else if (months < 3) r.Tip("Сервер молодой. Главное сейчас — не бросать: выкладывать прогресс каждый вайп и набирать людей. Все самые маленькие из изученных серверов созданы 1–3 месяца назад.", 2);
            }

            // Бусты
            long b = Json.Long(g, "premium_subscription_count", 0);
            r.Stat("Бусты", b.ToString(), "у лидеров: AOD 48, B2 34, .CL 24");
            r.Crit(1, b >= 14 ? 1 : b >= 7 ? 0.75 : b >= 2 ? 0.45 : b >= 1 ? 0.25 : 0);
            if (b >= 14) r.Plus("Много бустов (" + b + "): люди готовы платить за сервер.", 2);
            else if (b == 0)
            {
                r.Minus("Никто не бустит сервер (0 бустов).", 2);
                r.Tip("Сделайте роль «Booster» с бонусами и канал «спасибо за буст»: у сильных кланов 24–48 бустов.", 2);
            }

            // Описание
            if (!string.IsNullOrEmpty(r.Description))
            {
                r.Plus("Есть описание сервера — его видно на карточке приглашения.", 3);
                r.Crit(1, 1);
            }
            else
            {
                r.Minus("Нет описания сервера на карточке приглашения.", 2);
                r.Tip("Добавьте описание сервера в 1–2 строки: кто вы и чем сильны (пример TROUBLE: «соревновательный клан, $2000+ выигрышей на турнирах»).", 2);
                r.Crit(1, 0);
            }

            // Возможности сервера
            var f = new HashSet<string>(Json.Arr(Json.Get(g, "features")).Select(x => Convert.ToString(x)));
            bool community = f.Contains("COMMUNITY");
            r.Crit(1, community ? 1 : 0);
            if (community) r.Plus("Включён режим «Сообщество»: экран правил, приветствие новичков, канал объявлений, статистика сервера.", 3);
            else r.Tip("Включите режим «Сообщество» в настройках сервера: появятся экран правил, вопросы для новичков (онбординг), канал объявлений и статистика сервера.", 1);

            bool onb = f.Contains("GUILD_ONBOARDING");
            r.Crit(0.5, onb ? 1 : 0);
            if (onb) r.Plus("Есть онбординг: новичок отвечает на вопросы и сразу получает нужные роли и каналы.", 3);
            else if (community) r.Tip("Настройте онбординг (вопросы для новичков при входе): новичок сразу видит нужные каналы. Так сделано у B2.", 2);

            if (!string.IsNullOrEmpty(r.Vanity))
            {
                r.Plus("Красивая ссылка discord.gg/" + r.Vanity + " — её легко запомнить и вставить в видео.", 3);
                r.Crit(0.5, 1);
            }
            else r.Crit(0.5, 0.3);

            bool hasBanner = !string.IsNullOrEmpty(Json.Str(g, "banner"));
            bool hasSplash = !string.IsNullOrEmpty(Json.Str(g, "splash"));
            if (hasBanner || hasSplash)
            {
                r.Plus("Есть оформление: " + (hasBanner && hasSplash ? "баннер и фон приглашения" : hasBanner ? "баннер" : "фон приглашения") + ".", 3);
                r.Crit(0.5, 1);
            }
            else
            {
                r.Tip("Добавьте баннер и фон приглашения (открываются бустами): сервер выглядит солидно ещё до входа.", 3);
                r.Crit(0.5, 0);
            }

            if (f.Contains("DISCOVERABLE")) r.Plus("Сервер есть в каталоге Discord: его находят через поиск серверов.", 2);

            if (string.IsNullOrEmpty(Json.Str(g, "icon")))
            {
                r.Minus("У сервера нет иконки.", 1);
                r.Tip("Поставьте узнаваемую иконку с тегом клана.", 1);
            }

            // Проверка новичков
            long vl = Json.Long(g, "verification_level", -1);
            string[] vn = { "нет", "низкий (подтверждённая почта)", "средний (5 минут в Discord)", "высокий (10 минут на сервере)", "очень высокий (телефон)" };
            if (vl >= 0 && vl <= 4)
            {
                r.Stat("Проверка новичков", vn[vl], null);
                if (vl == 0)
                {
                    r.Minus("Нет проверки новичков: сервер открыт для спам-ботов.", 1);
                    r.Tip("Поставьте уровень проверки «Средний» в настройках безопасности сервера.", 1);
                    r.Crit(1, 0.3);
                }
                else if (vl == 4)
                {
                    r.Minus("Для входа нужен подтверждённый телефон — часть новичков не сможет зайти.", 2);
                    r.Crit(1, 0.5);
                }
                else
                {
                    r.Plus("Есть защита от спам-ботов (проверка: " + vn[vl] + ").", 3);
                    r.Crit(1, 1);
                }
            }
        }

        static void EvalSettings(Report r, Dictionary<string, object> g)
        {
            long notif = Json.Long(g, "default_message_notifications", 0);
            if (notif == 0 && r.Members > 200)
            {
                r.Minus("Уведомления по умолчанию — «Все сообщения». На большом сервере это раздражает: люди выключают звук сервера или уходят.", 2);
                r.Tip("В настройках сервера поставьте стандартные уведомления «Только @упоминания».", 2);
                r.Crit(0.5, 0);
            }
            else r.Crit(0.5, 1);

            long filter = Json.Long(g, "explicit_content_filter", 0);
            if (filter == 0)
            {
                r.Tip("Включите фильтр откровенного контента в настройках модерации: на серверах кланов много подростков.", 2);
                r.Crit(0.5, 0);
            }
            else r.Crit(0.5, 1);

            r.Stat("Свои эмодзи", Json.Arr(Json.Get(g, "emojis")).Length.ToString(), null);
        }

        // ---------- Каналы ----------

        static List<ChannelInfo> ParseChannels(object[] arr)
        {
            var list = new List<ChannelInfo>();
            foreach (var x in arr)
            {
                var d = Json.Obj(x);
                if (d == null) continue;
                var c = new ChannelInfo();
                c.Id = Json.Str(d, "id");
                c.Name = Json.Str(d, "name") ?? "";
                c.Norm = Util.Norm(c.Name);
                c.Type = (int)Json.Long(d, "type", 0);
                c.Position = (int)Json.Long(d, "position", 0);
                c.ParentId = Json.Str(d, "parent_id");
                string lm = Json.Str(d, "last_message_id");
                if (!string.IsNullOrEmpty(lm)) c.Last = Util.FromSnowflake(lm);
                list.Add(c);
            }
            var cats = new Dictionary<string, ChannelInfo>();
            foreach (var c in list) if (c.Type == 4 && c.Id != null) cats[c.Id] = c;
            foreach (var c in list)
            {
                ChannelInfo parent;
                if (c.ParentId != null && cats.TryGetValue(c.ParentId, out parent))
                {
                    c.ParentName = parent.Name;
                    c.ParentNorm = parent.Norm;
                }
            }
            return list;
        }

        public static bool IsTextLike(int type) { return type == 0 || type == 5 || type == 15 || type == 16; }
        public static bool IsVoice(int type) { return type == 2 || type == 13; }

        static string ListNames(IEnumerable<ChannelInfo> list, int max)
        {
            var all = list.ToList();
            var names = all.Take(max).Select(c => "#" + c.Name).ToList();
            string s = string.Join(", ", names);
            if (all.Count > max) s += " и ещё " + (all.Count - max);
            return s;
        }

        static void EvalChannels(Report r, List<ChannelInfo> chans, Dictionary<string, object> g)
        {
            r.Channels = chans;
            var text = chans.Where(c => IsTextLike(c.Type)).ToList();
            var voice = chans.Where(c => IsVoice(c.Type)).ToList();
            int cats = chans.Count(c => c.Type == 4);
            r.Stat("Каналы", text.Count + " текстовых, " + voice.Count + " голосовых, " + cats + " категорий", "у изученных серверов в среднем около 40 каналов");

            var now = DateTime.Now;
            foreach (var c in text)
            {
                c.IsPartner = c.ParentNorm != null && Regex.IsMatch(c.ParentNorm, PartnerCatRx, RO) &&
                              !Regex.IsMatch(c.Norm, "price|прайс|info|инфо|rule|правил", RO);
                c.IsInfo = Regex.IsMatch(c.Norm, InfoRx, RO);
                if (c.Last == null) c.Status = "пустой";
                else
                {
                    double days = (now - c.Last.Value).TotalDays;
                    c.Status = days <= 7 ? "живой" : days <= 30 ? "спит" : "молчит";
                }
            }

            int live = text.Count(c => c.Status == "живой");
            r.Stat("Живые каналы", live + " из " + text.Count, "были сообщения за последние 7 дней");

            var regular = text.Where(c => !c.IsInfo && !c.IsPartner).ToList();
            if (regular.Count > 0)
            {
                int regLive = regular.Count(c => c.Status == "живой");
                double share = (double)regLive / regular.Count;
                r.Crit(2, share >= 0.5 ? 1 : share * 2);
                if (share >= 0.5) r.Plus("Больше половины обычных каналов живые (" + regLive + " из " + regular.Count + " писали за неделю).", 2);
                else r.Minus("Мало живых каналов: только " + regLive + " из " + regular.Count + " обычных каналов писали за неделю.", 1);
            }

            var empty = text.Where(c => c.Status == "пустой" && !c.IsPartner).ToList();
            if (empty.Count > 0)
            {
                r.Minus("Пустые каналы (ни одного сообщения): " + ListNames(empty, 12) + ".", 1);
                r.Tip("Заполните или скройте пустые каналы. Пустой канал говорит новичку: «здесь ничего не происходит».", 1);
            }
            var dead = regular.Where(c => c.Status == "молчит").ToList();
            if (dead.Count > 0) r.Minus("Давно молчат (больше 30 дней): " + ListNames(dead, 12) + ".", 2);

            if (text.Count + voice.Count > 70)
            {
                r.Minus("Очень много каналов (" + (text.Count + voice.Count) + "). Новичку трудно разобраться, где что.", 2);
                r.Tip("Объедините похожие каналы и спрячьте лишние: у сильных кланов обычно 30–60 каналов.", 2);
            }
            else if (text.Count < 8)
                r.Tip("Каналов мало (" + text.Count + " текстовых). Добавьте основные разделы: новости, прогресс вайпа, медиа, розыгрыши, поиск напарников.", 2);
            if (voice.Count < 3) r.Tip("Добавьте голосовые каналы разного размера (main, max 2/3/4/5) и AFK.", 3);

            int partners = text.Count(c => c.IsPartner);
            if (partners > 0) r.Stat("Каналы партнёров и рекламы", partners.ToString(), "у лидеров 2–15 (у B2 — платная реклама), у средних кланов 10–17 обменных");
            if (partners > 10)
            {
                r.Minus("Много каналов партнёров и рекламы (" + partners + "). Обмен рекламой с мелкими кланами почти не даёт новых людей: у всех одна и та же аудитория.", 2);
                r.Tip("Оставьте 2–3 крупных партнёра (не меньше вашего сервера), остальным предлагайте платную рекламу.", 2);
            }

            KeyChannelsCheck(r, chans, g);
        }

        static void KeyChannelsCheck(Report r, List<ChannelInfo> chans, Dictionary<string, object> g)
        {
            string rulesId = Json.Str(g, "rules_channel_id");
            var missCore = new List<string>();
            int found = 0;
            foreach (var k in Keys)
            {
                ChannelInfo hit = null;
                if (k.Title == "Правила" && !string.IsNullOrEmpty(rulesId))
                    hit = chans.FirstOrDefault(c => c.Id == rulesId);
                if (hit == null)
                {
                    string rx = k.Rx;
                    var matches = chans.Where(c => (k.Voice ? IsVoice(c.Type) : IsTextLike(c.Type)) && !c.IsPartner && Regex.IsMatch(c.Norm, rx, RO)).ToList();
                    if (k.Exclude != null)
                    {
                        string ex = k.Exclude;
                        hit = matches.FirstOrDefault(c => !Regex.IsMatch(c.Norm, ex, RO));
                    }
                    if (hit == null) hit = matches.FirstOrDefault();
                }
                if (hit != null)
                {
                    found++;
                    r.KeyChecks.Add(new[] { k.Title, (k.Voice ? "🔊 " : "#") + hit.Name, k.Why });
                    r.Crit(k.W, 1);
                }
                else
                {
                    r.KeyChecks.Add(new[] { k.Title, null, k.Why });
                    r.Crit(k.W, 0);
                    r.Tip("Добавьте канал «" + k.Title + "»: " + k.Why, k.W >= 1 ? 1 : 2);
                    if (k.W >= 1) missCore.Add(k.Title);
                }
            }
            if (found >= Keys.Length * 0.75) r.Plus("Есть почти все ключевые каналы (" + found + " из " + Keys.Length + ").", 2);
            if (missCore.Count > 0) r.Minus("Нет важных каналов: " + string.Join(", ", missCore) + ".", 1);
        }

        // ---------- Роли ----------

        static bool IsBot(Dictionary<string, object> member)
        {
            return Json.Bool(Json.Obj(Json.Get(member, "user")), "bot");
        }

        static bool HasTag(Dictionary<string, object> role, string tag)
        {
            var t = Json.Obj(Json.Get(role, "tags"));
            return t != null && Json.Has(t, tag);
        }

        static void EvalRoles(Report r, object[] roles, List<Dictionary<string, object>> members, string guildId)
        {
            var list = roles.Select(x => Json.Obj(x)).Where(d => d != null).OrderByDescending(d => Json.Long(d, "position", 0)).ToList();
            var everyone = list.FirstOrDefault(d => Json.Str(d, "id") == guildId);
            var humanRoles = list.Where(d => Json.Str(d, "id") != guildId && !HasTag(d, "bot_id")).ToList();
            int hoisted = humanRoles.Count(d => Json.Bool(d, "hoist"));
            r.Stat("Роли", humanRoles.Count + " (отдельно в списке участников: " + hoisted + ")", "без учёта ролей ботов");

            Dictionary<string, int> counts = null;
            if (members != null)
            {
                counts = new Dictionary<string, int>();
                foreach (var mbr in members)
                    foreach (var rid in Json.Arr(Json.Get(mbr, "roles")))
                    {
                        string k = Convert.ToString(rid, CultureInfo.InvariantCulture);
                        int c;
                        counts.TryGetValue(k, out c);
                        counts[k] = c + 1;
                    }
            }

            r.RoleRows = new List<string[]>();
            foreach (var d in humanRoles.Take(40))
            {
                string id = Json.Str(d, "id");
                string cnt = counts == null ? "—" : (counts.ContainsKey(id) ? counts[id].ToString() : "0");
                string note = Json.Bool(d, "hoist") ? "показывается отдельно" : "";
                if (HasTag(d, "premium_subscriber")) note = "роль бустеров";
                r.RoleRows.Add(new[] { Json.Str(d, "name"), cnt, note });
            }

            var names = humanRoles.Select(d => Util.Norm(Json.Str(d, "name"))).ToList();
            int staff = names.Count(n => Regex.IsMatch(n, StaffRx, RO));
            r.Crit(0.5, Math.Min(1, staff / 3.0));
            if (staff >= 3) r.Plus("Есть роли персонала (" + staff + "): владелец, админы, модераторы или тикет-стафф.", 3);
            else r.Tip("Сделайте отдельные роли персонала: Admin, Moderator, Ticket Staff. У всех лидеров есть отдельные люди на заявки.", 2);

            bool ex = names.Any(n => Regex.IsMatch(n, ExRx, RO));
            r.Crit(0.5, ex ? 1 : 0);
            if (ex) r.Plus("Есть роль для бывших игроков: ушедшие остаются на сервере зрителями.", 3);
            else r.Tip("Сделайте роль «Ex / бывший игрок» вместо выгона: ушедшие остаются зрителями (у ECLIPSE 18 человек с такой ролью).", 2);

            bool media = names.Any(n => Regex.IsMatch(n, MediaRx, RO));
            r.Crit(0.5, media ? 1 : 0);
            if (media) r.Plus("Есть роли для медийных людей (Media / YouTube / VIP): это мотивирует снимать контент про клан.", 3);
            else r.Tip("Добавьте роли Media / YouTube: они мотивируют участников снимать видео про клан — главный источник новых людей.", 2);

            if (everyone != null)
            {
                ulong p;
                ulong.TryParse(Json.Str(everyone, "permissions"), out p);
                var bad = DangerPerms.Where(kv => (p & kv.Key) != 0).Select(kv => kv.Value).ToList();
                if (bad.Count > 0)
                {
                    r.Minus("ОПАСНО: у роли @everyone (это все участники) есть права: " + string.Join(", ", bad) + ". Любой новичок может навредить серверу.", 0);
                    r.Tip("Уберите опасные права у роли @everyone (Настройки сервера → Роли → @everyone).", 0);
                    r.Crit(2, 0);
                }
                else
                {
                    r.Plus("У роли @everyone нет опасных прав.", 3);
                    r.Crit(2, 1);
                }
            }

            if (members != null)
            {
                int humans = members.Count(x => !IsBot(x));
                int noRole = members.Count(x => !IsBot(x) && Json.Arr(Json.Get(x, "roles")).Length == 0);
                if (humans > 0)
                {
                    double share = (double)noRole / humans;
                    r.Stat("Участники без ролей", Util.N(noRole) + " (" + Math.Round(share * 100) + "%)", null);
                    if (share > 0.3) r.Tip("У многих участников нет ни одной роли. Выдавайте роль Member автоматически (через онбординг или бота), чтобы новички сразу видели каналы.", 2);
                }
            }
        }

        // ---------- Боты ----------

        static void EvalBots(Report r, object[] roles, List<Dictionary<string, object>> members, string selfId)
        {
            var names = new List<string>();
            foreach (var x in roles)
            {
                var d = Json.Obj(x);
                if (d == null || !HasTag(d, "bot_id")) continue;
                if (selfId != null && Json.Str(Json.Obj(Json.Get(d, "tags")), "bot_id") == selfId) continue;
                names.Add(Json.Str(d, "name"));
            }
            if (members != null)
            {
                foreach (var mbr in members)
                {
                    if (!IsBot(mbr)) continue;
                    var u = Json.Obj(Json.Get(mbr, "user"));
                    if (selfId != null && Json.Str(u, "id") == selfId) continue;
                    string n = Json.Str(u, "global_name") ?? Json.Str(u, "username");
                    if (n != null && !names.Any(z => string.Equals(z, n, StringComparison.OrdinalIgnoreCase))) names.Add(n);
                }
            }
            r.BotNames = names;
            r.Stat("Боты", names.Count.ToString(), null);

            foreach (var bd in BotDefs)
            {
                string rx = bd.Rx;
                string hit = names.FirstOrDefault(n => Regex.IsMatch(Util.Norm(n), rx, RO));
                r.BotChecks.Add(new[] { bd.Title, hit, bd.Rec });
                r.Crit(0.5, hit != null ? 1 : 0);
                if (hit == null) r.Tip("Поставьте бота для задачи «" + bd.Title + "»: " + bd.Rec + ".", 2);
            }
            if (names.Count > 15)
                r.Tip("Ботов много (" + names.Count + "). Лишние боты засоряют список участников и могут быть опасны: уберите тех, кем не пользуетесь.", 2);
        }

        // ---------- Участники ----------

        static List<Dictionary<string, object>> LoadMembers(DiscordClient api, string gid, Action<string> progress)
        {
            var list = new List<Dictionary<string, object>>();
            string after = "0";
            for (int page = 0; page < 50; page++)
            {
                var arr = Json.Arr(api.Get("/guilds/" + gid + "/members?limit=1000&after=" + after));
                if (arr.Length == 0) break;
                foreach (var x in arr)
                {
                    var d = Json.Obj(x);
                    if (d != null) list.Add(d);
                }
                after = Json.Str(Json.Obj(Json.Get(Json.Obj(arr[arr.Length - 1]), "user")), "id");
                progress("Читаю участников: " + list.Count + "…");
                if (arr.Length < 1000 || after == null) break;
            }
            return list;
        }

        static void EvalMembers(Report r, List<Dictionary<string, object>> members)
        {
            var now = DateTime.Now;
            var humans = members.Where(x => !IsBot(x)).ToList();
            r.Stat("Люди / боты", Util.N(humans.Count) + " / " + (members.Count - humans.Count), "по списку участников");
            int j7 = 0, j30 = 0;
            var weeks = new int[12];
            foreach (var mb in humans)
            {
                var jd = Util.ParseIso(Json.Str(mb, "joined_at"));
                if (jd == null) continue;
                double days = (now - jd.Value).TotalDays;
                if (days <= 7) j7++;
                if (days <= 30) j30++;
                int w = (int)(days / 7);
                if (w >= 0 && w < 12) weeks[11 - w]++;
            }
            r.JoinWeeks = weeks;
            r.Stat("Новые за 7 дней", "+" + Util.N(j7), "считаются только те, кто остался на сервере");
            r.Stat("Новые за 30 дней", "+" + Util.N(j30), null);
            long baseCount = Math.Max(humans.Count, r.Members > 0 ? r.Members - (members.Count - humans.Count) : 0);
            double share = baseCount > 0 ? (double)j30 / baseCount : 0;
            r.Crit(2, share >= 0.10 ? 1 : share >= 0.05 ? 0.7 : share >= 0.02 ? 0.4 : 0.15);
            if (share >= 0.10) r.Plus("Хороший приток: за месяц пришли и остались " + Util.N(j30) + " человек (" + Math.Round(share * 100) + "% сервера).", 2);
            else if (share < 0.03)
            {
                r.Minus("Слабый приток: за месяц пришли и остались только " + Util.N(j30) + " человек.", 1);
                r.Tip("Нужен внешний поток людей: видео после каждого вайпа, свой канал набора на большом сервере, розыгрыш за приглашения.", 1);
            }
        }

        // ---------- Ссылки-приглашения ----------

        static void EvalInvites(Report r, object[] invites)
        {
            var list = invites.Select(x => Json.Obj(x)).Where(d => d != null).OrderByDescending(d => Json.Long(d, "uses", 0)).ToList();
            long total = list.Sum(d => Json.Long(d, "uses", 0));
            r.Stat("Ссылок-приглашений", list.Count.ToString(), "всего переходов по ним: " + Util.N(total));
            r.InviteRows = new List<string[]>();
            foreach (var d in list.Take(15))
            {
                var u = Json.Obj(Json.Get(d, "inviter"));
                var ch = Json.Obj(Json.Get(d, "channel"));
                long maxAge = Json.Long(d, "max_age", 0);
                r.InviteRows.Add(new[]
                {
                    Json.Str(d, "code"),
                    ch != null ? "#" + Json.Str(ch, "name") : "",
                    u != null ? (Json.Str(u, "global_name") ?? Json.Str(u, "username")) : "",
                    Util.N(Json.Long(d, "uses", 0)),
                    maxAge == 0 ? "бессрочная" : "временная"
                });
            }
            if (list.Count > 0 && total > 0)
            {
                long tu = Json.Long(list[0], "uses", 0);
                if (tu * 2 >= total)
                    r.Plus("Больше всего людей пришло по ссылке discord.gg/" + Json.Str(list[0], "code") + " (" + Util.N(tu) + " из " + Util.N(total) + ").", 3);
            }
            if (list.Count <= 2)
                r.Tip("Делайте отдельную ссылку-приглашение для каждого места рекламы (каждый партнёр, TikTok, YouTube). Так будет видно, откуда приходят люди.", 2);
        }

        // ---------- Активность в чатах ----------

        static void EvalActivity(Report r, DiscordClient api, List<ChannelInfo> chans, Action<string> progress)
        {
            var now = DateTime.Now;
            var cands = chans.Where(c => (c.Type == 0 || c.Type == 5) && c.Last.HasValue && (now - c.Last.Value).TotalDays <= 7 && !c.IsPartner)
                             .OrderByDescending(c => c.Last.Value).Take(8).ToList();
            if (cands.Count == 0)
            {
                r.Minus("За последнюю неделю почти никто не писал в текстовых каналах.", 1);
                r.Crit(2, 0);
                return;
            }
            int msgs = 0, read = 0;
            bool capped = false;
            var authors = new HashSet<string>();
            foreach (var c in cands)
            {
                progress("Считаю сообщения: #" + c.Name + "…");
                try
                {
                    var arr = Json.Arr(api.Get("/channels/" + c.Id + "/messages?limit=100"));
                    read++;
                    int inWeek = 0;
                    foreach (var x in arr)
                    {
                        var d = Json.Obj(x);
                        var ts = Util.ParseIso(Json.Str(d, "timestamp"));
                        if (ts == null || (now - ts.Value).TotalDays > 7) continue;
                        inWeek++;
                        var a = Json.Obj(Json.Get(d, "author"));
                        if (a != null && !Json.Bool(a, "bot")) authors.Add(Json.Str(a, "id"));
                    }
                    msgs += inWeek;
                    if (arr.Length == 100 && inWeek == 100) capped = true;
                }
                catch (ApiException) { }
            }
            if (read == 0)
            {
                r.Notes.Add("Сообщения не прочитаны: у бота нет права «Читать историю сообщений» в каналах.");
                return;
            }
            r.Stat("Сообщений за 7 дней", (capped ? "больше " : "") + Util.N(msgs), "в " + read + " самых живых каналах (до 100 последних в каждом)");
            r.Stat("Пишут за неделю", Util.N(authors.Count) + " чел.", null);
            double need = Math.Max(10, (r.Members > 0 ? r.Members : 300) * 0.03);
            double v = Math.Min(1, authors.Count / need);
            r.Crit(2, v);
            if (v >= 1) r.Plus("Живое общение: за неделю писали " + authors.Count + " человек.", 2);
            else if (v < 0.4)
            {
                r.Minus("Мало кто пишет: за неделю сообщения оставили только " + authors.Count + " человек.", 1);
                r.Tip("Оживите чат: мемы, «похвастайся», клипы участников, вопросы-ответы, итоги вайпа с обсуждением.", 2);
            }
        }

        // ---------- История и общие советы ----------

        static void HistoryCompare(Report r)
        {
            try
            {
                var last = History.Last(r.Id);
                if (last != null && r.Members >= 0)
                {
                    DateTime when;
                    long pm, po;
                    if (DateTime.TryParseExact(last[0], "yyyy-MM-dd HH:mm", Util.Inv, DateTimeStyles.None, out when) &&
                        long.TryParse(last[3], out pm) && long.TryParse(last[4], out po))
                    {
                        long dm = r.Members - pm;
                        double days = (DateTime.Now - when).TotalDays;
                        string ds = days < 1 ? "меньше суток" : Math.Round(days) + " дн.";
                        r.Stat("С прошлой проверки", (dm >= 0 ? "+" : "") + Util.N(dm) + " участников",
                               "проверка " + when.ToString("dd.MM.yyyy HH:mm") + " (" + ds + " назад): было " + Util.N(pm) + (po >= 0 ? ", в сети " + Util.N(po) : ""));
                        if (days >= 1 && dm > 0) r.Plus("Рост с прошлой проверки: +" + Util.N(dm) + " за " + ds, 2);
                        else if (days >= 1 && dm < 0) r.Minus("Отток с прошлой проверки: " + Util.N(dm) + " за " + ds, 1);
                    }
                }
                if (r.Members >= 0) History.Add(r.Id, r.Name, r.Members, r.Online);
            }
            catch { }
        }

        static void GeneralTips(Report r, bool inviteMode)
        {
            r.Tip("Снимайте короткие видео после каждого вайпа (TikTok, Shorts, YouTube) со ссылкой на Discord. Это главный источник людей у SOSO GANG и .CL.", 3);
            r.Tip("Своя ссылка-приглашение для каждого места рекламы + бот учёта приглашений. У TROUBLE TEAM по одной такой ссылке пришёл 341 человек.", 3);
            r.Tip("Розыгрыш раз в 2–4 недели с условием «пригласи 2–3 друзей» и канал «выданные призы», чтобы люди верили.", 3);
            r.Tip("Участвуйте в турнирах и публикуйте результаты: победы — лучшая реклама (AOD: 1 место на турнире на 25 000 ₽).", 3);
            if (r.Members >= 0 && r.Members < 1000)
                r.Tip("Обменивайтесь рекламой только с серверами не меньше вашего: 2–3 сильных партнёра лучше 15 мелких.", 3);
            else
                r.Tip("Продавайте рекламу как услугу: прайс 400–1 500 ₽, промокоды, отзывы, роль «ads buyer» (пример B2).", 3);
            if (inviteMode) r.Tip("Сделайте часть для Европы (eu-chat, ticket-eu): у всех лидеров два языка.", 3);
            r.Tip("Не берите в спонсоры кейс-сайты: это азартные игры, а на серверах кланов много подростков.", 3);
        }
    }
}
