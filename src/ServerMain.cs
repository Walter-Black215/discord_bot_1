#if NET
using System;
using System.IO;
using System.Threading;

namespace DiscordAnalyzer
{
    // Серверная версия бота (Linux, без окна). Собирается только проектом server/DiscordAnalyzerBot.csproj
    public static class ServerProgram
    {
        static void Log(string msg)
        {
            Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg);
        }

        public static int Main(string[] args)
        {
            // Проверка без интернета: dotnet DiscordAnalyzerBot.dll --selftest out.html
            if (args.Length >= 2 && args[0] == "--selftest")
            {
                File.WriteAllText(args[1], HtmlReport.Build(SelfTest.Run()));
                Console.WriteLine("selftest ok: " + args[1]);
                return 0;
            }

            // Токен берём из переменной окружения или из файла data/token.txt (права 600)
            string tokenFile = Path.Combine(Paths.Data, "token.txt");
            string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
            if (string.IsNullOrWhiteSpace(token) && File.Exists(tokenFile)) token = File.ReadAllText(tokenFile);
            token = (token ?? "").Trim();
            if (token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) token = token.Substring(4).Trim();
            if (token.Length < 20)
            {
                Log("Нет токена бота. Положите его в " + tokenFile);
                return 2;
            }

            var done = new ManualResetEventSlim(false);
            var bot = new CommandBot(token, Log);
            bot.Stopped += () => done.Set();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; bot.Stop(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { bot.Stop(); done.Wait(5000); };

            try
            {
                bot.Start().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log("Не удалось запустить бота: " + ex.Message);
                return 1;
            }
            Log("Бот работает. Остановить: systemctl stop discord-analyzer");
            done.Wait();
            return 1;
        }
    }
}
#endif
