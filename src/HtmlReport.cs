using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace DiscordAnalyzer
{
    // Отчёт в виде HTML-страницы: показывается в окне программы и сохраняется в папку «Отчёты»
    public static class HtmlReport
    {
        static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        const string Css =
            "body{margin:0;background:#14110f;color:#f2ece6;font-family:'Segoe UI',Arial,sans-serif;font-size:14px}" +
            ".wrap{padding:22px 28px 40px;max-width:1100px}" +
            "h1{font-size:26px;margin:4px 0 6px;font-weight:bold}" +
            "h2{font-size:18px;color:#f4a259;margin:28px 0 10px;border-bottom:1px solid #3a322d;padding-bottom:6px;font-weight:bold}" +
            ".kicker{color:#e8692c;font-weight:bold;letter-spacing:1px;text-transform:uppercase;font-size:11px}" +
            ".muted{color:#b3a89e}.small{font-size:12px}" +
            "table{border-collapse:collapse;width:100%}" +
            ".head td{vertical-align:middle}" +
            ".icon{width:90px}.icon img{width:72px;height:72px;border-radius:16px}" +
            ".scorecell{width:170px}" +
            ".score{text-align:center;background:#1e1a17;border:1px solid #3a322d;border-radius:14px;padding:12px}" +
            ".sc{font-size:46px;font-weight:bold;color:#e8692c;line-height:1}.grade{font-size:16px;font-weight:bold;margin-top:4px}" +
            ".stats td{padding:7px 10px;border-bottom:1px solid #2c2622;vertical-align:top}" +
            ".stats td.k{color:#b3a89e;width:200px}.stats td.v{font-weight:bold;width:250px}" +
            ".cols td.col{vertical-align:top;width:50%;padding:0 10px 0 0}" +
            ".box{background:#1e1a17;border:1px solid #3a322d;border-radius:12px;padding:12px 16px;margin-bottom:10px}" +
            ".good{border-left:4px solid #5bbf7a}.bad{border-left:4px solid #e05a4f}.tipb{border-left:4px solid #e8b931}" +
            "ul,ol{margin:4px 0 0 0;padding-left:22px}li{margin:6px 0;line-height:1.45}" +
            ".t th{color:#f4a259;text-align:left;font-size:12px;text-transform:uppercase;padding:6px 8px;border-bottom:1px solid #3a322d}" +
            ".t td{padding:6px 8px;border-bottom:1px solid #2c2622;vertical-align:top}" +
            ".t tr.cat td{color:#f4a259;font-weight:bold;font-size:12px;padding-top:12px}" +
            ".ok{color:#5bbf7a;font-weight:bold}.no{color:#e05a4f;font-weight:bold}.warn{color:#e8b931;font-weight:bold}" +
            ".bar{background:#272220;height:12px;border-radius:6px}.fill{background:#e8692c;height:12px;border-radius:6px}" +
            "tr.me td{background:#3a2a1f;font-weight:bold}" +
            ".chart td{vertical-align:bottom;text-align:center;padding:0 4px}.chart tr.bars td{height:170px}" +
            ".cbar{background:#e8692c;width:30px;margin:0 auto;border-radius:4px 4px 0 0}" +
            ".note{font-size:12px;color:#b3a89e;margin:6px 0}" +
            ".card{background:#1e1a17;border:1px solid #3a322d;border-radius:12px;padding:14px 18px;vertical-align:top}" +
            "a{color:#f4a259}" +
            ".step{display:inline-block;background:#e8692c;color:#1a120c;font-weight:bold;width:24px;height:24px;line-height:24px;text-align:center;border-radius:12px;margin-right:8px}";

        static string Head(string title)
        {
            return "<!DOCTYPE html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"utf-8\">" +
                   "<title>" + H(title) + "</title><style>" + Css + "</style></head><body><div class=\"wrap\">";
        }

        const string Foot = "</div></body></html>";

        // Увеличение под масштаб экрана Windows (только для показа в окне программы)
        public static string WithZoom(string html, int percent)
        {
            if (percent <= 100) return html;
            int i = html.IndexOf("</style>", StringComparison.Ordinal);
            if (i < 0) return html;
            return html.Substring(0, i) + "body{zoom:" + percent + "%}" + html.Substring(i);
        }

        public static string Build(Report r)
        {
            var sb = new StringBuilder();
            sb.Append(Head("Анализ: " + r.Name));

            sb.Append("<table class=\"head\"><tr>");
            if (!string.IsNullOrEmpty(r.IconUrl)) sb.Append("<td class=\"icon\"><img src=\"" + H(r.IconUrl) + "\" alt=\"\"></td>");
            sb.Append("<td><div class=\"kicker\">" + H(r.Mode) + " · " + r.Generated.ToString("dd.MM.yyyy HH:mm") + "</div>");
            sb.Append("<h1>" + H(r.Name) + "</h1>");
            if (!string.IsNullOrEmpty(r.Description)) sb.Append("<div class=\"muted\">" + H(r.Description) + "</div>");
            if (!string.IsNullOrEmpty(r.InviteCode)) sb.Append("<div class=\"muted small\">Ссылка: discord.gg/" + H(r.InviteCode) + "</div>");
            sb.Append("</td><td class=\"scorecell\"><div class=\"score\"><div class=\"sc\">" + r.Score + "</div><div class=\"grade\">" + H(r.Grade) +
                      "</div><div class=\"muted small\">общая оценка из 100</div></div></td></tr></table>");

            sb.Append("<h2>Основная статистика</h2><table class=\"stats\">");
            foreach (var s in r.Stats)
                sb.Append("<tr><td class=\"k\">" + H(s[0]) + "</td><td class=\"v\">" + H(s[1]) + "</td><td class=\"muted\">" + H(s[2]) + "</td></tr>");
            sb.Append("</table>");

            if (r.Members >= 0) Compare(sb, r);

            sb.Append("<table class=\"cols\"><tr><td class=\"col\"><h2>Плюсы (" + r.Pluses.Count + ")</h2>");
            Items(sb, r.Pluses, "good", "Явных плюсов не нашлось.");
            sb.Append("</td><td class=\"col\"><h2>Минусы (" + r.Minuses.Count + ")</h2>");
            Items(sb, r.Minuses, "bad", "Серьёзных минусов не нашлось.");
            sb.Append("</td></tr></table>");

            sb.Append("<h2>Советы по продвижению</h2>");
            TipGroup(sb, "В первую очередь", r.Tips.Where(x => x.Prio <= 1).OrderBy(x => x.Prio).ToList(), true);
            TipGroup(sb, "Что ещё улучшить", r.Tips.Where(x => x.Prio == 2).ToList(), false);
            TipGroup(sb, "Общие советы по росту (из опыта лидеров)", r.Tips.Where(x => x.Prio >= 3).ToList(), false);

            if (r.KeyChecks.Count > 0)
            {
                sb.Append("<h2>Ключевые каналы</h2><table class=\"t\"><tr><th></th><th>Что</th><th>Найдено</th><th>Зачем нужно</th></tr>");
                foreach (var k in r.KeyChecks)
                {
                    bool ok = k[1] != null;
                    sb.Append("<tr><td class=\"" + (ok ? "ok\">✓" : "no\">✗") + "</td><td>" + H(k[0]) + "</td><td>" +
                              (ok ? H(k[1]) : "<span class=\"no\">нет</span>") + "</td><td class=\"muted\">" + H(k[2]) + "</td></tr>");
                }
                sb.Append("</table><div class=\"note\">Каналы узнаются по названию. Если канал есть, но назван необычно, программа может его не узнать.</div>");
            }

            if (r.JoinWeeks != null) Chart(sb, r.JoinWeeks);

            if (r.BotNames != null)
            {
                sb.Append("<h2>Боты</h2><div class=\"box\">" +
                          (r.BotNames.Count == 0 ? "Ботов не найдено." : "На сервере: " + H(string.Join(", ", r.BotNames))) + "</div>");
                sb.Append("<table class=\"t\"><tr><th></th><th>Задача</th><th>Найден</th><th>Что обычно ставят</th></tr>");
                foreach (var b in r.BotChecks)
                {
                    bool ok = b[1] != null;
                    sb.Append("<tr><td class=\"" + (ok ? "ok\">✓" : "no\">✗") + "</td><td>" + H(b[0]) + "</td><td>" +
                              (ok ? H(b[1]) : "<span class=\"no\">нет</span>") + "</td><td class=\"muted\">" + H(b[2]) + "</td></tr>");
                }
                sb.Append("</table>");
            }

            if (r.RoleRows != null && r.RoleRows.Count > 0)
            {
                sb.Append("<h2>Роли (сверху вниз)</h2><table class=\"t\"><tr><th>Роль</th><th>Людей</th><th></th></tr>");
                foreach (var row in r.RoleRows)
                    sb.Append("<tr><td>" + H(row[0]) + "</td><td>" + H(row[1]) + "</td><td class=\"muted\">" + H(row[2]) + "</td></tr>");
                sb.Append("</table>");
            }

            if (r.InviteRows != null && r.InviteRows.Count > 0)
            {
                sb.Append("<h2>Ссылки-приглашения: откуда приходят люди</h2><table class=\"t\"><tr><th>Ссылка</th><th>Канал</th><th>Кто создал</th><th>Переходов</th><th>Срок</th></tr>");
                foreach (var row in r.InviteRows)
                    sb.Append("<tr><td>discord.gg/" + H(row[0]) + "</td><td>" + H(row[1]) + "</td><td>" + H(row[2]) + "</td><td><b>" + H(row[3]) +
                              "</b></td><td class=\"muted\">" + H(row[4]) + "</td></tr>");
                sb.Append("</table>");
            }

            if (r.Channels != null) Channels(sb, r.Channels);

            if (r.Notes.Count > 0)
            {
                sb.Append("<h2>Что важно знать</h2><div class=\"box\"><ul>");
                foreach (var n in r.Notes) sb.Append("<li class=\"muted\">" + H(n) + "</li>");
                sb.Append("</ul></div>");
            }

            sb.Append("<div class=\"note\" style=\"margin-top:28px\">Оценка и советы основаны на анализе 38 Discord-серверов Rust-кланов и 122 серверов-партнёров (данные на " +
                      Bench.Date + "). Программа только читает данные и ничего не меняет на сервере.</div>");
            sb.Append(Foot);
            return sb.ToString();
        }

        static void TipGroup(StringBuilder sb, string title, List<Finding> items, bool marks)
        {
            if (items.Count == 0) return;
            sb.Append("<div class=\"box tipb\"><div class=\"kicker\" style=\"margin-bottom:4px\">" + H(title) + "</div><ol>");
            foreach (var t in items)
            {
                string mark = marks && t.Prio == 0 ? "<span class=\"no\">Срочно: </span>" : "";
                sb.Append("<li>" + mark + H(t.Text) + "</li>");
            }
            sb.Append("</ol></div>");
        }

        static void Items(StringBuilder sb, List<Finding> items, string cls, string empty)
        {
            sb.Append("<div class=\"box " + cls + "\">");
            if (items.Count == 0) sb.Append("<div class=\"muted\">" + H(empty) + "</div>");
            else
            {
                sb.Append("<ul>");
                foreach (var f in items.OrderBy(x => x.Prio)) sb.Append("<li>" + H(f.Text) + "</li>");
                sb.Append("</ul>");
            }
            sb.Append("</div>");
        }

        static string Key(string s)
        {
            var b = new StringBuilder();
            foreach (char c in Util.Norm(s)) if (char.IsLetterOrDigit(c)) b.Append(c);
            return b.ToString();
        }

        static void Compare(StringBuilder sb, Report r)
        {
            var rows = Bench.Clans.ToList();
            string myKey = Key(r.Name);
            int fi = rows.FindIndex(x => (!string.IsNullOrEmpty(x.Id) && x.Id == r.Id) || (myKey.Length >= 3 && Key(x.Name) == myKey));
            Bench.Srv mine;
            if (fi >= 0)
            {
                mine = new Bench.Srv(r.Id, rows[fi].Name, (int)r.Members, (int)r.Online);
                rows[fi] = mine;
            }
            else
            {
                mine = new Bench.Srv(r.Id, r.Name, (int)r.Members, (int)r.Online);
                rows.Add(mine);
            }
            rows = rows.OrderByDescending(x => x.M).ToList();
            int idx = rows.IndexOf(mine);
            int below = rows.Count(x => x != mine && x.M < r.Members);
            int pct = (int)Math.Round(100.0 * below / Math.Max(1, rows.Count - 1));

            sb.Append("<h2>Сравнение с Rust-кланами</h2>");
            sb.Append("<div>Место <b>" + (idx + 1) + "</b> из " + rows.Count + " по числу участников. Сервер больше, чем <b>" + pct + "%</b> изученных клановых серверов.</div>");
            sb.Append("<div class=\"bar\" style=\"margin:10px 0 14px\"><div class=\"fill\" style=\"width:" + Math.Max(2, pct) + "%\"></div></div>");

            var show = new SortedSet<int>();
            for (int i = 0; i < 3 && i < rows.Count; i++) show.Add(i);
            for (int i = idx - 3; i <= idx + 3; i++) if (i >= 0 && i < rows.Count) show.Add(i);
            show.Add(rows.Count - 1);

            sb.Append("<table class=\"t\"><tr><th>Место</th><th>Сервер</th><th>Участников</th><th>В сети</th><th>% в сети</th></tr>");
            int prev = -1;
            foreach (int i in show)
            {
                if (prev >= 0 && i > prev + 1) sb.Append("<tr><td colspan=\"5\" class=\"muted\">…</td></tr>");
                var x = rows[i];
                bool me = x == mine;
                sb.Append("<tr" + (me ? " class=\"me\"" : "") + "><td>" + (i + 1) + "</td><td>" + H(x.Name) + (me ? " &nbsp;← этот сервер" : "") +
                          "</td><td>" + (x.Approx ? "~" : "") + Util.N(x.M) + "</td><td>" + (x.O >= 0 ? Util.N(x.O) : "—") + "</td><td>" +
                          (x.O >= 0 && x.M > 0 ? Math.Round(100.0 * x.O / x.M) + "%" : "—") + "</td></tr>");
                prev = i;
            }
            sb.Append("</table><div class=\"note\">Данные других серверов — на " + Bench.Date + ". Число «в сети» зависит от времени суток.</div>");
        }

        static void Chart(StringBuilder sb, int[] weeks)
        {
            int max = Math.Max(1, weeks.Max());
            sb.Append("<h2>Новые участники по неделям</h2><div class=\"box\"><table class=\"chart\"><tr class=\"bars\">");
            foreach (int v in weeks)
            {
                int h = (int)Math.Round(140.0 * v / max);
                sb.Append("<td><div class=\"small\">" + v + "</div><div class=\"cbar\" style=\"height:" + Math.Max(2, h) + "px\"></div></td>");
            }
            sb.Append("</tr><tr>");
            for (int i = 0; i < weeks.Length; i++)
                sb.Append("<td class=\"muted small\">" + (i == weeks.Length - 1 ? "эта" : "−" + (weeks.Length - 1 - i)) + "</td>");
            sb.Append("</tr></table><div class=\"note\">Считаются только те, кто ещё на сервере. «−3» — три недели назад, «эта» — последние 7 дней.</div></div>");
        }

        static string TypeName(int t)
        {
            switch (t)
            {
                case 0: return "текст";
                case 2: return "голос";
                case 5: return "объявления";
                case 13: return "сцена";
                case 15: return "форум";
                case 16: return "медиа";
                default: return "другое";
            }
        }

        static void Channels(StringBuilder sb, List<ChannelInfo> chans)
        {
            sb.Append("<h2>Каналы и их активность</h2><table class=\"t\"><tr><th>Канал</th><th>Тип</th><th>Последнее сообщение</th><th>Статус</th></tr>");
            var cats = chans.Where(c => c.Type == 4).OrderBy(c => c.Position).ToList();
            var catIds = new HashSet<string>(cats.Select(c => c.Id));
            ChannelRows(sb, chans.Where(c => c.Type != 4 && (c.ParentId == null || !catIds.Contains(c.ParentId))));
            foreach (var cat in cats)
            {
                sb.Append("<tr class=\"cat\"><td colspan=\"4\">" + H(cat.Name) + "</td></tr>");
                string catId = cat.Id;
                ChannelRows(sb, chans.Where(c => c.ParentId == catId));
            }
            sb.Append("</table><div class=\"note\">«живой» — писали за 7 дней, «спит» — 8–30 дней, «молчит» — больше 30 дней, «пустой» — ни одного сообщения. " +
                      "Каналы-объявления (правила, инфо, партнёры) обновляются редко — для них это нормально.</div>");
        }

        static void ChannelRows(StringBuilder sb, IEnumerable<ChannelInfo> list)
        {
            foreach (var ch in list.OrderBy(z => Analyzer.IsVoice(z.Type) ? 1 : 0).ThenBy(z => z.Position))
            {
                bool text = Analyzer.IsTextLike(ch.Type);
                string last = text ? (ch.Last.HasValue ? ch.Last.Value.ToString("dd.MM.yyyy") + " (" + Util.Ago(ch.Last.Value) + ")" : "—") : "";
                string status = "", cls = "muted";
                if (text)
                {
                    status = ch.Status ?? "";
                    if (status == "живой") cls = "ok";
                    else if (ch.IsInfo || ch.IsPartner) { cls = "muted"; status += ch.IsPartner ? " · партнёр/реклама" : " · инфо"; }
                    else if (status == "спит") cls = "warn";
                    else cls = "no";
                }
                string icon = text ? "# " : (Analyzer.IsVoice(ch.Type) && !ch.Name.Contains("🔊") ? "🔊 " : "");
                sb.Append("<tr><td>" + icon + H(ch.Name) + "</td><td class=\"muted\">" + TypeName(ch.Type) +
                          "</td><td>" + H(last) + "</td><td class=\"" + cls + "\">" + H(status) + "</td></tr>");
            }
        }

        // ---------- Служебные страницы ----------

        public static string Welcome()
        {
            var sb = new StringBuilder();
            sb.Append(Head("Анализатор Discord-серверов"));
            sb.Append("<div class=\"kicker\">Добро пожаловать</div><h1>Анализатор Discord-серверов</h1>");
            sb.Append("<div class=\"muted\">Показывает статистику сервера, плюсы и минусы, сравнение с Rust-кланами и советы по продвижению.</div>");
            sb.Append("<table style=\"margin-top:22px;border-collapse:separate;border-spacing:14px 0\"><tr>");
            sb.Append("<td class=\"card\" style=\"width:50%\"><h2 style=\"margin-top:0\">По ссылке-приглашению</h2><ul>" +
                      "<li>Подходит для <b>любого</b> сервера, вступать не нужно.</li>" +
                      "<li>Видно: участники, сколько в сети, бусты, возраст, скорость роста, настройки, сравнение с 48 кланами.</li>" +
                      "<li>Вставьте ссылку вида <b>discord.gg/abc123</b> и нажмите «Анализировать».</li></ul></td>");
            sb.Append("<td class=\"card\" style=\"width:50%\"><h2 style=\"margin-top:0\">Мой сервер (через бота)</h2><ul>" +
                      "<li>Полный разбор <b>вашего</b> сервера: каналы, пустые и мёртвые каналы, роли, боты, прирост по неделям, активность в чатах, ссылки-приглашения.</li>" +
                      "<li>Нужен свой бот (это бесплатно, 5 минут). Нажмите «Как создать бота?».</li></ul></td>");
            sb.Append("</tr></table>");
            sb.Append("<div class=\"card\" style=\"margin:14px 14px 0\"><h2 style=\"margin-top:0\">Бот с командой /анализ</h2><ul>" +
                      "<li>Запустите своего бота прямо из программы — и админы любых серверов, куда он добавлен, смогут сами писать <b>/анализ</b> и получать отчёт в Discord.</li>" +
                      "<li>Бот работает, пока программа открыта.</li></ul></div>");
            sb.Append("<div class=\"note\" style=\"margin-top:18px\">Каждый отчёт сохраняется в папку «Отчёты» рядом с программой. Программа запоминает прошлые проверки и показывает рост с прошлого раза.</div>");
            sb.Append(Foot);
            return sb.ToString();
        }

        public static string Help(string addBotUrl)
        {
            var sb = new StringBuilder();
            sb.Append(Head("Как создать бота"));
            sb.Append("<div class=\"kicker\">Инструкция</div><h1>Как создать бота для анализа своего сервера</h1>");
            sb.Append("<div class=\"muted\">Бот только читает данные. Он ничего не пишет и не меняет на сервере. Добавить бота можно только на сервер, где у вас есть право «Управлять сервером».</div>");
            sb.Append("<div class=\"box\" style=\"margin-top:18px\">");
            sb.Append("<p><span class=\"step\">1</span>Откройте <a href=\"https://discord.com/developers/applications\">discord.com/developers/applications</a> и войдите в свой Discord.</p>");
            sb.Append("<p><span class=\"step\">2</span>Нажмите <b>New Application</b>, введите имя (например, «Анализатор») и нажмите <b>Create</b>.</p>");
            sb.Append("<p><span class=\"step\">3</span>Слева откройте раздел <b>Bot</b>. Нажмите <b>Reset Token</b> и скопируйте токен. <span class=\"no\">Токен — это пароль бота: никому его не показывайте.</span></p>");
            sb.Append("<p><span class=\"step\">4</span>Там же, в блоке <b>Privileged Gateway Intents</b>, включите <b>Server Members Intent</b> и сохраните. Это нужно для прироста участников и подсчёта людей в ролях. Другие переключатели не нужны.</p>");
            sb.Append("<p><span class=\"step\">5</span>Вставьте токен в программу (вкладка «Мой сервер») и нажмите <b>Подключить</b>.</p>");
            sb.Append("<p><span class=\"step\">6</span>Нажмите <b>Добавить бота на сервер</b>, выберите свой сервер и подтвердите. Бот попросит права: «Просматривать каналы», «Читать историю сообщений» и «Управлять сервером». Последнее нужно только чтобы видеть ссылки-приглашения; его можно снять.</p>");
            sb.Append("<p><span class=\"step\">7</span>Вернитесь в программу, снова нажмите <b>Подключить</b>, выберите сервер в списке и нажмите <b>Анализировать</b>.</p>");
            sb.Append("</div>");
            if (!string.IsNullOrEmpty(addBotUrl))
                sb.Append("<div class=\"box good\">Ссылка для добавления вашего бота: <a href=\"" + H(addBotUrl) + "\">" + H(addBotUrl) + "</a></div>");
            sb.Append("<div class=\"note\">Бот не обязан быть «запущен»: программа заходит от его имени только во время анализа. Если отметить «Запомнить токен», он хранится зашифрованным в папке «Данные» и открывается только под вашей учётной записью Windows.</div>");
            sb.Append(Foot);
            return sb.ToString();
        }

        public static string BotLog(List<string> entries, bool running, string inviteUrl)
        {
            var sb = new StringBuilder();
            sb.Append(Head("Бот с командой /анализ"));
            sb.Append("<div class=\"kicker\">Бот с командой /анализ</div><h1>" + (running ? "<span class=\"ok\">● Бот запущен</span>" : "<span class=\"muted\">○ Бот не запущен</span>") + "</h1>");
            sb.Append("<div class=\"box\" style=\"margin-top:14px\"><ol>" +
                      "<li>Вставьте токен бота и нажмите <b>«Запустить бота»</b>. Программа должна оставаться открытой: пока она закрыта, бот не отвечает.</li>" +
                      "<li>Админ любого сервера, куда добавлен бот, пишет в любом канале <b>/анализ</b> (в английском Discord — <b>/analyze</b>).</li>" +
                      "<li>Бот присылает оценку, плюсы, минусы, советы и полный отчёт файлом. Ответ видит только тот, кто вызвал команду.</li>" +
                      "<li>Чтобы бота добавили на другие серверы, нажмите <b>«Скопировать ссылку для серверов»</b> и отправьте ссылку их админам.</li></ol>" +
                      "<div class=\"note\">Команда появляется только у тех, у кого есть право «Управлять сервером». После первого запуска она может появиться в Discord не сразу — перезапустите Discord (Ctrl+R).</div></div>");
            if (!string.IsNullOrEmpty(inviteUrl))
                sb.Append("<div class=\"box good\">Ссылка для добавления бота: <span class=\"muted\">" + H(inviteUrl) + "</span></div>");
            sb.Append("<h2>Журнал</h2><div class=\"box\">");
            if (entries.Count == 0) sb.Append("<div class=\"muted\">Пока пусто.</div>");
            else
            {
                sb.Append("<table class=\"t\">");
                for (int i = entries.Count - 1; i >= 0; i--) sb.Append("<tr><td>" + H(entries[i]) + "</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("</div>");
            sb.Append(Foot);
            return sb.ToString();
        }

        public static string Message(string title, string text)
        {
            return Head(title) + "<div class=\"kicker\">Сообщение</div><h1>" + H(title) + "</h1><div class=\"box bad\">" + H(text) + "</div>" + Foot;
        }

        public static string Busy(string text)
        {
            return Head("Анализ") + "<h1>Идёт анализ…</h1><div class=\"muted\">" + H(text) + "</div>" + Foot;
        }
    }
}
