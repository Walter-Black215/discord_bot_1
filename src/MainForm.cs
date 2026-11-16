using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscordAnalyzer
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // Служебный режим для проверки: DiscordAnalyzer.exe --invite <код> --out <файл.html>
            if (args.Length >= 2 && args[0] == "--invite")
            {
                string outPath = args.Length >= 4 && args[2] == "--out" ? args[3] : "report.html";
                try
                {
                    var r = Analyzer.AnalyzeInvite(args[1], s => { }, false);
                    File.WriteAllText(outPath, HtmlReport.Build(r), new UTF8Encoding(true));
                    return 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(outPath + ".error.txt", ex.ToString(), new UTF8Encoding(true));
                    return 1;
                }
            }
            // Служебный режим: проверка сообщения бота на тестовых данных
            if (args.Length >= 2 && args[0] == "--selftest-embed")
            {
                try
                {
                    File.WriteAllText(args[1], Json.Serialize(CommandBot.BuildPayload(SelfTest.Run())), new UTF8Encoding(false));
                    return 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1] + ".error.txt", ex.ToString(), new UTF8Encoding(true));
                    return 1;
                }
            }
            // Служебный режим: проверка полного разбора на тестовых данных
            if (args.Length >= 2 && args[0] == "--selftest")
            {
                try
                {
                    File.WriteAllText(args[1], HtmlReport.Build(SelfTest.Run()), new UTF8Encoding(true));
                    return 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1] + ".error.txt", ex.ToString(), new UTF8Encoding(true));
                    return 1;
                }
            }

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Непредвиденная ошибка: " + e.Exception.Message, "Анализатор Discord", MessageBoxButtons.OK, MessageBoxIcon.Error);
            // Служебный режим: открыть окно и сразу проанализировать ссылку (--auto-invite <код>)
            string auto = args.Length >= 2 && args[0] == "--auto-invite" ? args[1] : null;
            Application.Run(new MainForm(auto));
            return 0;
        }
    }

    public class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(20, 17, 15);
        static readonly Color Panel1 = Color.FromArgb(30, 26, 23);
        static readonly Color Panel2 = Color.FromArgb(39, 34, 32);
        static readonly Color Line = Color.FromArgb(58, 50, 45);
        static readonly Color TextC = Color.FromArgb(242, 236, 230);
        static readonly Color Muted = Color.FromArgb(179, 168, 158);
        static readonly Color Accent = Color.FromArgb(232, 105, 44);

        Button modeInvite, modeBot, modeServe, btnInvite, btnConnect, btnBot, btnAddBot, btnOpen, btnFolder, btnServe, btnCopyInvite;
        Panel pInvite, pBot, pServe;
        TextBox txtInvite, txtToken, txtToken2;
        CheckBox chkRemember, chkRemember2;
        CommandBot cmdBot;
        readonly System.Collections.Generic.List<string> botLog = new System.Collections.Generic.List<string>();
        int mode;
        ComboBox cmbGuilds;
        LinkLabel lnkHelp;
        WebBrowser browser;
        Label lblStatus;
        ProgressBar prg;

        string lastReportPath, botId, token;
        int zoom = 100;
        bool busy;

        public MainForm() : this(null) { }

        public MainForm(string autoInvite)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Анализатор Discord-серверов";
            ClientSize = new Size(1180, 800);
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = TextC;
            Font = new Font("Segoe UI", 10f);

            // Шапка
            var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Panel1 };
            header.Controls.Add(new Label { Text = "Анализатор Discord-серверов", Font = new Font("Segoe UI Semibold", 16f), ForeColor = TextC, AutoSize = true, Location = new Point(18, 6) });
            header.Controls.Add(new Label { Text = "Статистика, плюсы и минусы, сравнение с Rust-кланами и советы по продвижению", ForeColor = Muted, AutoSize = true, Location = new Point(21, 40) });

            // Переключатель режимов
            var modes = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Bg };
            modeInvite = MakeButton("По ссылке-приглашению", new Point(18, 10), 220, false);
            modeBot = MakeButton("Мой сервер (через бота)", new Point(246, 10), 230, false);
            modeServe = MakeButton("Бот с командой /анализ", new Point(484, 10), 230, false);
            modeInvite.Click += (s, e) => SetMode(0);
            modeBot.Click += (s, e) => SetMode(1);
            modeServe.Click += (s, e) => SetMode(2);
            modes.Controls.Add(modeInvite);
            modes.Controls.Add(modeBot);
            modes.Controls.Add(modeServe);

            // Поля ввода
            var inputs = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = Bg };

            pInvite = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
            pInvite.Controls.Add(MakeLabel("Ссылка-приглашение или код:", new Point(18, 4)));
            txtInvite = MakeBox(new Point(20, 28), 520);
            txtInvite.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await RunInvite(); } };
            btnInvite = MakeButton("Анализировать", new Point(552, 26), 160, true);
            btnInvite.Click += async (s, e) => await RunInvite();
            var hint1 = MakeLabel("Подходит для любого сервера, вступать не нужно. Видны общие цифры: участники, в сети, бусты, возраст, настройки.", new Point(18, 66));
            hint1.ForeColor = Muted;
            txtInvite.Name = "txtInvite";
            btnInvite.Name = "btnInvite";
            pInvite.Controls.Add(txtInvite);
            pInvite.Controls.Add(btnInvite);
            pInvite.Controls.Add(hint1);

            pBot = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Visible = false };
            pBot.Controls.Add(MakeLabel("Токен бота:", new Point(18, 4)));
            txtToken = MakeBox(new Point(20, 28), 380);
            txtToken.UseSystemPasswordChar = true;
            btnConnect = MakeButton("Подключить", new Point(412, 26), 140, false);
            btnConnect.Click += async (s, e) => await ConnectBot();
            chkRemember = new CheckBox { Text = "Запомнить токен (зашифрован)", ForeColor = Muted, AutoSize = true, Location = new Point(566, 31), BackColor = Bg };
            lnkHelp = new LinkLabel { Text = "Как создать бота?", AutoSize = true, Location = new Point(830, 31), LinkColor = Color.FromArgb(244, 162, 89), ActiveLinkColor = Accent, BackColor = Bg };
            lnkHelp.LinkClicked += (s, e) => ShowHtml(HtmlReport.Help(AddBotUrl()));
            cmbGuilds = new ComboBox { Location = new Point(20, 68), Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Panel2, ForeColor = TextC, FlatStyle = FlatStyle.Flat };
            cmbGuilds.Items.Add("— сначала подключите бота —");
            cmbGuilds.SelectedIndex = 0;
            btnBot = MakeButton("Анализировать", new Point(412, 64), 140, true);
            btnBot.Click += async (s, e) => await RunBot();
            btnAddBot = MakeButton("Добавить бота на сервер", new Point(566, 64), 230, false);
            btnAddBot.Click += (s, e) =>
            {
                string url = AddBotUrl();
                if (url == null) { Status("Сначала подключите бота: вставьте токен и нажмите «Подключить»."); return; }
                OpenExternal(url);
            };
            pBot.Controls.Add(txtToken);
            pBot.Controls.Add(btnConnect);
            pBot.Controls.Add(chkRemember);
            pBot.Controls.Add(lnkHelp);
            pBot.Controls.Add(cmbGuilds);
            pBot.Controls.Add(btnBot);
            pBot.Controls.Add(btnAddBot);

            // Режим 3: бот с командой /анализ
            pServe = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Visible = false };
            pServe.Controls.Add(MakeLabel("Токен бота:", new Point(18, 4)));
            txtToken2 = MakeBox(new Point(20, 28), 380);
            txtToken2.UseSystemPasswordChar = true;
            btnServe = MakeButton("Запустить бота", new Point(412, 26), 180, true);
            btnServe.Click += async (s, e) => await ToggleServe();
            chkRemember2 = new CheckBox { Text = "Запомнить токен (зашифрован)", ForeColor = Muted, AutoSize = true, Location = new Point(606, 31), BackColor = Bg };
            btnCopyInvite = MakeButton("Скопировать ссылку для серверов", new Point(412, 64), 300, false);
            btnCopyInvite.Click += (s, e) =>
            {
                string url = cmdBot != null ? CommandBot.InviteUrl(cmdBot.AppId) : null;
                if (url == null) { Status("Сначала запустите бота — тогда появится ссылка для добавления."); return; }
                try { Clipboard.SetText(url); Status("Ссылка скопирована. Её можно дать админам других серверов."); }
                catch { Status("Ссылка: " + url); }
            };
            var hint3 = MakeLabel("Пока бот запущен, админы серверов с ботом пишут /анализ.\nОтвет видит только тот, кто вызвал команду.", new Point(18, 62));
            hint3.ForeColor = Muted;
            pServe.Controls.Add(txtToken2);
            pServe.Controls.Add(btnServe);
            pServe.Controls.Add(chkRemember2);
            pServe.Controls.Add(btnCopyInvite);
            pServe.Controls.Add(hint3);

            inputs.Controls.Add(pInvite);
            inputs.Controls.Add(pBot);
            inputs.Controls.Add(pServe);

            // Нижняя строка
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Panel1, Width = 1180 };
            lblStatus = new Label { Name = "lblStatus", Text = "Готово к работе.", ForeColor = Muted, AutoEllipsis = true, Location = new Point(18, 14), Size = new Size(560, 22), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            prg = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Location = new Point(590, 15), Size = new Size(140, 18), Anchor = AnchorStyles.Right | AnchorStyles.Top, Visible = false };
            btnOpen = MakeButton("Открыть отчёт в браузере", new Point(742, 8), 230, false);
            btnOpen.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnOpen.Click += (s, e) =>
            {
                if (lastReportPath != null && File.Exists(lastReportPath)) OpenExternal(lastReportPath);
                else Status("Сначала сделайте анализ — отчёт появится здесь.");
            };
            btnFolder = MakeButton("Папка отчётов", new Point(982, 8), 180, false);
            btnFolder.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnFolder.Click += (s, e) =>
            {
                Directory.CreateDirectory(Paths.Reports);
                OpenExternal(Paths.Reports);
            };
            bottom.Controls.Add(lblStatus);
            bottom.Controls.Add(prg);
            bottom.Controls.Add(btnOpen);
            bottom.Controls.Add(btnFolder);

            // Окно с отчётом
            browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                ScriptErrorsSuppressed = true,
                AllowWebBrowserDrop = false,
                IsWebBrowserContextMenuEnabled = true,
                WebBrowserShortcutsEnabled = true
            };
            browser.Navigating += (s, e) =>
            {
                if (e.Url != null && (e.Url.Scheme == "http" || e.Url.Scheme == "https"))
                {
                    e.Cancel = true;
                    OpenExternal(e.Url.ToString());
                }
            };

            Controls.Add(browser);
            Controls.Add(bottom);
            Controls.Add(inputs);
            Controls.Add(modes);
            Controls.Add(header);
            ResumeLayout(false);

            Load += (s, e) =>
            {
                try
                {
                    using (var gr = CreateGraphics()) zoom = (int)Math.Round(gr.DpiX / 96.0 * 100);
                }
                catch { zoom = 100; }
                string saved = TokenStore.Load();
                if (!string.IsNullOrEmpty(saved))
                {
                    txtToken.Text = saved;
                    txtToken2.Text = saved;
                    chkRemember.Checked = true;
                    chkRemember2.Checked = true;
                }
                SetMode(0);
                ShowHtml(HtmlReport.Welcome());
                txtInvite.Focus();
            };
            FormClosing += (s, e) => { if (cmdBot != null) cmdBot.Stop(); };
            Shown += async (s, e) =>
            {
                if (string.IsNullOrEmpty(autoInvite)) return;
                txtInvite.Text = autoInvite;
                await RunInvite();
            };
        }

        // ---------- Элементы оформления ----------

        Button MakeButton(string text, Point loc, int width, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Location = loc,
                Size = new Size(width, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Panel2,
                ForeColor = primary ? Color.FromArgb(26, 18, 12) : TextC,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Line;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(244, 130, 70) : Color.FromArgb(52, 45, 41);
            if (primary) b.Font = new Font("Segoe UI Semibold", 10f);
            return b;
        }

        Label MakeLabel(string text, Point loc)
        {
            return new Label { Text = text, Location = loc, AutoSize = true, ForeColor = TextC, BackColor = Bg };
        }

        TextBox MakeBox(Point loc, int width)
        {
            return new TextBox { Location = loc, Width = width, BackColor = Panel2, ForeColor = TextC, BorderStyle = BorderStyle.FixedSingle };
        }

        void SetMode(int m)
        {
            mode = m;
            pInvite.Visible = m == 0;
            pBot.Visible = m == 1;
            pServe.Visible = m == 2;
            var buttons = new[] { modeInvite, modeBot, modeServe };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].BackColor = i == m ? Line : Panel2;
                buttons[i].FlatAppearance.BorderColor = i == m ? Accent : Line;
            }
            if (m == 0) txtInvite.Focus();
            else if (m == 1) txtToken.Focus();
            else
            {
                if (txtToken2.Text.Length == 0) txtToken2.Text = txtToken.Text;
                ShowBotLog();
                if (cmdBot == null || !cmdBot.IsRunning) txtToken2.Focus();
            }
        }

        // ---------- Бот с командой /анализ ----------

        void ShowBotLog()
        {
            bool on = cmdBot != null && cmdBot.IsRunning;
            ShowHtml(HtmlReport.BotLog(botLog, on, on ? CommandBot.InviteUrl(cmdBot.AppId) : null));
        }

        void BotLog(string text)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    botLog.Add(DateTime.Now.ToString("dd.MM HH:mm:ss") + "  " + text);
                    if (botLog.Count > 300) botLog.RemoveAt(0);
                    if (mode == 2) ShowBotLog();
                }));
            }
            catch { }
        }

        async Task ToggleServe()
        {
            if (cmdBot != null && cmdBot.IsRunning)
            {
                cmdBot.Stop();
                return;
            }
            string t = txtToken2.Text.Trim();
            if (t.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) t = t.Substring(4).Trim();
            if (t.Length < 20) { Status("Вставьте токен бота."); return; }
            btnServe.Enabled = false;
            Status("Запускаю бота…");
            var bot = new CommandBot(t, BotLog);
            bot.Stopped += () =>
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        btnServe.Text = "Запустить бота";
                        btnServe.Enabled = true;
                        Status("Бот остановлен.");
                        if (mode == 2) ShowBotLog();
                    }));
                }
                catch { }
            };
            try
            {
                await bot.Start();
                cmdBot = bot;
                if (chkRemember2.Checked) TokenStore.Save(t);
                btnServe.Text = "Остановить бота";
                Status("Бот «" + bot.BotName + "» запущен. Напишите /анализ на своём сервере. Не закрывайте программу, пока бот нужен.");
                ShowBotLog();
            }
            catch (Exception ex)
            {
                string msg = Friendly(ex);
                Status(msg);
                ShowHtml(HtmlReport.Message("Не удалось запустить бота", msg));
            }
            finally
            {
                btnServe.Enabled = true;
            }
        }

        void Status(string text) { lblStatus.Text = text; }

        void SetBusy(bool on)
        {
            busy = on;
            prg.Visible = on;
            btnInvite.Enabled = !on;
            btnBot.Enabled = !on;
            btnConnect.Enabled = !on;
            UseWaitCursor = on;
        }

        void ShowHtml(string html)
        {
            browser.DocumentText = HtmlReport.WithZoom(html, zoom);
        }

        static void OpenExternal(string target)
        {
            try { Process.Start(target); }
            catch (Exception ex) { MessageBox.Show("Не удалось открыть: " + ex.Message, "Анализатор Discord"); }
        }

        string AddBotUrl()
        {
            if (string.IsNullOrEmpty(botId)) return null;
            // Права: просматривать каналы (1024) + читать историю сообщений (65536) + управлять сервером (32, только чтобы видеть приглашения)
            return CommandBot.InviteUrl(botId);
        }

        static string Friendly(Exception ex)
        {
            var a = ex as ApiException;
            if (a == null) return ex.Message;
            if (a.Status == 0) return "Нет связи с Discord. Проверьте интернет. (" + a.Message + ")";
            if (a.Code == 10006) return "Приглашение не найдено: ссылка неверная, истекла или её удалили.";
            if (a.Status == 401) return "Токен бота не подходит. Скопируйте его заново на сайте Discord Developer Portal (раздел Bot → Reset Token).";
            if (a.Code == 10004) return "Сервер не найден. Возможно, бота удалили с этого сервера.";
            if (a.Status == 403) return "Нет доступа (" + a.Message + "). Проверьте, что бот добавлен на сервер и у него есть право «Просматривать каналы».";
            if (a.Status == 404) return "Не найдено: " + a.Message;
            if (a.Status == 429) return a.Message;
            return "Discord ответил ошибкой " + a.Status + ": " + a.Message;
        }

        // ---------- Действия ----------

        async Task RunInvite()
        {
            if (busy) return;
            string input = txtInvite.Text.Trim();
            if (input.Length == 0) { Status("Вставьте ссылку-приглашение, например https://discord.gg/abc123"); return; }
            SetBusy(true);
            Status("Анализирую…");
            IProgress<string> progress = new Progress<string>(Status);
            try
            {
                Report r = await Task.Run(() => Analyzer.AnalyzeInvite(input, s => progress.Report(s), true));
                ShowReport(r);
            }
            catch (Exception ex)
            {
                string msg = Friendly(ex);
                Status(msg);
                ShowHtml(HtmlReport.Message("Не получилось проанализировать", msg));
            }
            finally { SetBusy(false); }
        }

        async Task ConnectBot()
        {
            if (busy) return;
            string t = txtToken.Text.Trim();
            if (t.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) t = t.Substring(4).Trim();
            if (t.Length < 20) { Status("Вставьте токен бота. Как его получить — нажмите «Как создать бота?»."); return; }
            SetBusy(true);
            Status("Подключаюсь к Discord…");
            try
            {
                var api = new DiscordClient(t);
                ConnectResult res = await Task.Run(() => Analyzer.Connect(api));
                token = t;
                botId = res.BotId;
                if (chkRemember.Checked) TokenStore.Save(t); else TokenStore.Forget();
                cmbGuilds.Items.Clear();
                foreach (var g in res.Guilds) cmbGuilds.Items.Add(g);
                if (res.Guilds.Count == 0)
                {
                    cmbGuilds.Items.Add("— бот ещё не добавлен ни на один сервер —");
                    cmbGuilds.SelectedIndex = 0;
                    Status("Бот «" + res.BotName + "» подключён, но ещё не добавлен на сервер. Нажмите «Добавить бота на сервер».");
                    ShowHtml(HtmlReport.Help(AddBotUrl()));
                }
                else
                {
                    cmbGuilds.SelectedIndex = 0;
                    Status("Бот «" + res.BotName + "» подключён. Серверов: " + res.Guilds.Count + ". Выберите сервер и нажмите «Анализировать».");
                }
            }
            catch (Exception ex)
            {
                string msg = Friendly(ex);
                Status(msg);
                ShowHtml(HtmlReport.Message("Не удалось подключить бота", msg));
            }
            finally { SetBusy(false); }
        }

        async Task RunBot()
        {
            if (busy) return;
            var g = cmbGuilds.SelectedItem as GuildItem;
            if (token == null || g == null) { Status("Сначала подключите бота и выберите сервер."); return; }
            SetBusy(true);
            Status("Анализирую сервер «" + g.Name + "»…");
            IProgress<string> progress = new Progress<string>(Status);
            try
            {
                var api = new DiscordClient(token);
                string gid = g.Id;
                Report r = await Task.Run(() => Analyzer.AnalyzeBot(api, gid, s => progress.Report(s), true));
                ShowReport(r);
            }
            catch (Exception ex)
            {
                string msg = Friendly(ex);
                Status(msg);
                ShowHtml(HtmlReport.Message("Не получилось проанализировать", msg));
            }
            finally { SetBusy(false); }
        }

        void ShowReport(Report r)
        {
            string html = HtmlReport.Build(r);
            ShowHtml(html);
            try
            {
                Directory.CreateDirectory(Paths.Reports);
                string path = Path.Combine(Paths.Reports, Util.SafeFileName(r.Name) + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") + ".html");
                File.WriteAllText(path, html, new UTF8Encoding(true));
                lastReportPath = path;
                Status("Готово. Оценка: " + r.Score + " из 100 (" + r.Grade + "). Отчёт сохранён в папку «Отчёты».");
            }
            catch (Exception ex)
            {
                Status("Готово, но отчёт не сохранился: " + ex.Message);
            }
        }
    }
}
