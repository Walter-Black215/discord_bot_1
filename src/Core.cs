using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
#if !NET
// Эти части есть только в Windows-версии с окном (на сервере Linux их нет)
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;
#endif

namespace DiscordAnalyzer
{
    // Ошибка, которую вернул Discord
    public class ApiException : Exception
    {
        public int Status;
        public int Code;
        public ApiException(int status, int code, string message) : base(message)
        {
            Status = status;
            Code = code;
        }
    }

    // Чтение JSON-ответов Discord
    public static class Json
    {
#if NET
        // Серверная версия (.NET 8, Linux)
        static readonly System.Text.Json.JsonSerializerOptions Opt = new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string Serialize(object o)
        {
            return System.Text.Json.JsonSerializer.Serialize(o, Opt);
        }

        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                using (var doc = System.Text.Json.JsonDocument.Parse(text)) return Conv(doc.RootElement);
            }
            catch { return null; }
        }

        static object Conv(System.Text.Json.JsonElement e)
        {
            switch (e.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    var d = new Dictionary<string, object>();
                    foreach (var p in e.EnumerateObject()) d[p.Name] = Conv(p.Value);
                    return d;
                case System.Text.Json.JsonValueKind.Array:
                    return e.EnumerateArray().Select(Conv).ToArray();
                case System.Text.Json.JsonValueKind.String:
                    return e.GetString();
                case System.Text.Json.JsonValueKind.Number:
                    long l;
                    if (e.TryGetInt64(out l)) return l;
                    return e.GetDouble();
                case System.Text.Json.JsonValueKind.True:
                    return true;
                case System.Text.Json.JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }
#else
        // Windows-версия с окном (.NET Framework)
        static readonly JavaScriptSerializer Ser = CreateSer();

        static JavaScriptSerializer CreateSer()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 256;
            return s;
        }

        public static string Serialize(object o)
        {
            return Ser.Serialize(o);
        }

        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try { return Ser.DeserializeObject(text); }
            catch { return null; }
        }
#endif

        public static Dictionary<string, object> Obj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static object[] Arr(object o)
        {
            if (o == null) return new object[0];
            var a = o as object[];
            if (a != null) return a;
            var al = o as ArrayList;
            if (al != null) return al.ToArray();
            return new object[0];
        }

        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v;
            return null;
        }

        public static bool Has(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key);
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            if (v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static long Long(Dictionary<string, object> d, string key, long def)
        {
            var v = Get(d, key);
            if (v == null) return def;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch
            {
                long r;
                return long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out r) ? r : def;
            }
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            return v is bool && (bool)v;
        }
    }

    // Запросы к Discord API (только чтение)
    public class DiscordClient
    {
        const string Base = "https://discord.com/api/v10";
        readonly string token;

        static DiscordClient()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        public DiscordClient(string botToken)
        {
            token = botToken;
        }

        public virtual object Get(string path)
        {
            return Request("GET", path, null, null, null);
        }

        // Отправка данных (ответ бота на команду)
        public object Send(string method, string path, object body)
        {
            return Request(method, path, body == null ? null : Json.Serialize(body), null, null);
        }

        // Отправка сообщения с прикреплённым файлом
        public object SendWithFile(string method, string path, object payload, string fileName, byte[] file)
        {
            return Request(method, path, Json.Serialize(payload), fileName, file);
        }

        static byte[] Multipart(string boundary, string json, string fileName, byte[] file)
        {
            using (var ms = new MemoryStream())
            {
                Action<string> w = s => { var b = Encoding.UTF8.GetBytes(s); ms.Write(b, 0, b.Length); };
                w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n");
                w(json);
                w("\r\n--" + boundary + "\r\nContent-Disposition: form-data; name=\"files[0]\"; filename=\"" + fileName + "\"\r\nContent-Type: text/html\r\n\r\n");
                ms.Write(file, 0, file.Length);
                w("\r\n--" + boundary + "--\r\n");
                return ms.ToArray();
            }
        }

        object Request(string method, string path, string json, string fileName, byte[] file)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var req = (HttpWebRequest)WebRequest.Create(Base + path);
                req.Method = method;
                req.UserAgent = "DiscordBot (https://discord.com, 1.0) ServerAnalyzer";
                req.Accept = "application/json";
                req.Timeout = 25000;
                req.ReadWriteTimeout = 25000;
                if (!string.IsNullOrEmpty(token)) req.Headers["Authorization"] = "Bot " + token;
                try
                {
                    if (json != null || file != null)
                    {
                        byte[] data;
                        if (file == null)
                        {
                            req.ContentType = "application/json";
                            data = Encoding.UTF8.GetBytes(json);
                        }
                        else
                        {
                            string boundary = "----analyzer" + Guid.NewGuid().ToString("N");
                            req.ContentType = "multipart/form-data; boundary=" + boundary;
                            data = Multipart(boundary, json ?? "{}", fileName, file);
                        }
                        req.ContentLength = data.Length;
                        using (var rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
                    }
                    using (var okResp = (HttpWebResponse)req.GetResponse())
                    using (var okReader = new StreamReader(okResp.GetResponseStream(), Encoding.UTF8))
                    {
                        return Json.Parse(okReader.ReadToEnd());
                    }
                }
                catch (WebException ex)
                {
                    var errResp = ex.Response as HttpWebResponse;
                    if (errResp == null) throw new ApiException(0, 0, ex.Message);
                    string body = "";
                    try
                    {
                        using (var errReader = new StreamReader(errResp.GetResponseStream(), Encoding.UTF8))
                            body = errReader.ReadToEnd();
                    }
                    catch { }
                    int status = (int)errResp.StatusCode;
                    errResp.Close();
                    var d = Json.Obj(Json.Parse(body));
                    if (status == 429)
                    {
                        double wait = 1;
                        object ra = Json.Get(d, "retry_after");
                        if (ra != null)
                        {
                            try { wait = Convert.ToDouble(ra, CultureInfo.InvariantCulture); } catch { }
                        }
                        Thread.Sleep((int)(Math.Min(wait, 20) * 1000) + 300);
                        continue;
                    }
                    string msg = Json.Str(d, "message");
                    throw new ApiException(status, (int)Json.Long(d, "code", 0),
                        string.IsNullOrEmpty(msg) ? ("HTTP " + status) : msg);
                }
            }
            throw new ApiException(429, 0, "Discord просит подождать: слишком много запросов. Попробуйте через минуту.");
        }
    }

    public static class Util
    {
        public static readonly CultureInfo Ru = new CultureInfo("ru-RU");
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static string N(double v)
        {
            return Math.Round(v).ToString("#,0", Ru);
        }

        // Дата создания по ID Discord (в ID зашито время)
        public static DateTime FromSnowflake(string id)
        {
            ulong v;
            if (string.IsNullOrEmpty(id) || !ulong.TryParse(id, out v)) return DateTime.MinValue;
            long ms = (long)(v >> 22) + 1420070400000L;
            return Epoch.AddMilliseconds(ms).ToLocalTime();
        }

        public static DateTime? ParseIso(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            DateTimeOffset d;
            if (DateTimeOffset.TryParse(s, Inv, DateTimeStyles.AssumeUniversal, out d)) return d.LocalDateTime;
            return null;
        }

        public static string Age(double days)
        {
            if (days < 30) return Math.Max(0, (int)days) + " дн.";
            double months = days / 30.44;
            if (months < 24) return Math.Round(months) + " мес.";
            return (months / 12).ToString("0.#", Ru) + " года";
        }

        public static string Ago(DateTime t)
        {
            double days = (DateTime.Now - t).TotalDays;
            if (days < 1) return "сегодня";
            if (days < 2) return "вчера";
            return Math.Floor(days) + " дн. назад";
        }

        // Приводим «красивые» шрифты (𝐁𝐎𝐋𝐃, ᴡᴇʟᴄᴏᴍᴇ) к обычным буквам, чтобы узнавать каналы по названию
        static readonly Dictionary<char, char> SmallCaps = BuildMap();

        static Dictionary<char, char> BuildMap()
        {
            string from = "ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘǫʀꜱᴛᴜᴠᴡʏᴢғ";
            string to = "abcdefghijklmnopqrstuvwyzf";
            var m = new Dictionary<char, char>();
            for (int i = 0; i < from.Length && i < to.Length; i++) m[from[i]] = to[i];
            return m;
        }

        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t;
            try { t = s.Normalize(NormalizationForm.FormKC); } catch { t = s; }
            var sb = new StringBuilder(t.Length);
            foreach (char ch in t)
            {
                char m;
                if (SmallCaps.TryGetValue(ch, out m)) sb.Append(m); else sb.Append(ch);
            }
            return sb.ToString().ToLowerInvariant();
        }

        public static string SafeFileName(string s)
        {
            string t = Norm(s ?? "server");
            var sb = new StringBuilder();
            foreach (char c in t)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.') sb.Append(c);
                else sb.Append(' ');
            }
            string r = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "\\s+", " ").Trim(' ', '.');
            if (r.Length > 50) r = r.Substring(0, 50).Trim();
            return r.Length == 0 ? "server" : r;
        }
    }

    public static class Paths
    {
#if NET
        public static string App { get { return AppContext.BaseDirectory; } }
        public static string Data { get { return Path.Combine(App, "data"); } }
        public static string Reports { get { return Path.Combine(App, "reports"); } }
#else
        public static string App { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        public static string Data { get { return Path.Combine(App, "Данные"); } }
        public static string Reports { get { return Path.Combine(App, "Отчёты"); } }
#endif
    }

    // История проверок: чтобы видеть рост от проверки к проверке
    public static class History
    {
        static string FilePath { get { return Path.Combine(Paths.Data, "history.csv"); } }

        public static string[] Last(string guildId)
        {
            if (!File.Exists(FilePath)) return null;
            string[] last = null;
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                var p = line.Split(';');
                if (p.Length >= 5 && p[1] == guildId) last = p;
            }
            return last;
        }

        static readonly object FileLock = new object();

        public static void Add(string guildId, string name, long members, long online)
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(Paths.Data);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm", Util.Inv) + ";" + guildId + ";" +
                              (name ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ") + ";" + members + ";" + online;
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
    }

#if !NET
    // Токен бота хранится зашифрованным (Windows DPAPI): прочитать можно только под вашей учётной записью
    public static class TokenStore
    {
        static string FilePath { get { return Path.Combine(Paths.Data, "token.bin"); } }

        public static void Save(string token)
        {
            Directory.CreateDirectory(Paths.Data);
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, enc);
        }

        public static string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                byte[] dec = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch { return null; }
        }

        public static void Forget()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }
    }
#endif

    // Публичные данные изученных клановых серверов (на 15.11.2026) — для сравнения
    public static class Bench
    {
        public class Srv
        {
            public string Id, Name;
            public int M, O;
            public bool Approx;
            public Srv(string id, string name, int m, int o) { Id = id; Name = name; M = m; O = o; }
        }

        public const string Date = "15.11.2026";

        public static readonly Srv[] Clans =
        {
            new Srv("1124740854028980306", "SOSO GANG", 4300, -1) { Approx = true },
            new Srv("1228319838104064130", ".ice | PUBLIC", 3241, 1024),
            new Srv("1354510949360468140", "B2 — community", 2738, 1124),
            new Srv("1444074543152103546", "ECLIPSE | RUST COMMUNITY", 2584, 1076),
            new Srv("1383202131875463170", "TROUBLE TEAM", 2209, 833),
            new Srv("1235618662392791070", "AOD SQUAD", 2175, 822),
            new Srv("1411371620479668357", "ERA | Public", 2168, -1),
            new Srv("1172875644321939518", ".CL - COMMUNITY", 2038, 842),
            new Srv("1369597804757909544", "Tank Community", 1659, 526),
            new Srv("", "MoonLight Rust Community", 1448, 369),
            new Srv("", "United Builders", 1428, 652),
            new Srv("", ".TRL & PUBLIC", 1400, 539),
            new Srv("", "WN | Recruitment", 1279, 305),
            new Srv("1270140271052455977", "322 TEAM | RUST", 1273, 334),
            new Srv("", "DG — Dominant Generation", 1188, 540),
            new Srv("", "KROTY TEAM", 1172, 567),
            new Srv("", "GRIN", 1142, 304),
            new Srv("", ".ØLD community Team 2", 1142, 334),
            new Srv("", "^MVR & COMMUNITY", 1135, 339),
            new Srv("", "SPG team", 1133, 156),
            new Srv("", "HD | Public channel", 1102, 358),
            new Srv("", "KVN Community", 1098, 205),
            new Srv("", ".INVISIBLE | COMMUNITY", 1058, 255),
            new Srv("", "EXTAZY TEAM | PUBLIC", 1030, 268),
            new Srv("1475541357430046901", "DWS - Death with a scythe", 1024, 216),
            new Srv("1467475250299146326", ".WR community", 1005, 247),
            new Srv("1487442399201333462", "KFG - kill for gear", 952, 228),
            new Srv("", "STAR", 943, 321),
            new Srv("1431747339768561838", "CLOWN TEAM COMMUNITY", 925, 276),
            new Srv("", "AIR TEAM", 918, 326),
            new Srv("", ".MIX Team Public", 879, 127),
            new Srv("1349736778462138439", "qR Clan | Rust", 779, 292),
            new Srv("1514323635635097841", "UERITY TEAM | COMMUNITY", 761, 297),
            new Srv("1534208179133743196", "B22 — Community", 750, 311),
            new Srv("1392412423213813820", "K4 | community", 744, 213),
            new Srv("1530149542119473284", "911 TEAM | PUBLIC", 689, 234),
            new Srv("1493642512458317884", "404 | Rust Aim Training", 685, 99),
            new Srv("1247592107623841924", "SIZE COMMUNITY", 683, 175),
            new Srv("1479531157191393330", "INFINITY COMMUNITY", 667, 175),
            new Srv("1476522494277062709", "MISS | CORD", 638, 179),
            new Srv("1526025469722492968", "LEGENDARY TEAM | COMMUNITY", 611, 220),
            new Srv("1510978608640823346", "DM clan | Public", 571, 188),
            new Srv("1467962211745136836", "CATWARE HEADQUARTERS", 370, 132),
            new Srv("1464571163630895361", "NW COMMUNITY", 345, -1),
            new Srv("1548063596850716813", "LR | COMMUNITY", 296, -1),
            new Srv("1501873296843477002", "NULL COMMUNITY", 279, -1),
            new Srv("1564429709343981648", "BWS Community", 127, -1),
            new Srv("1564036183703691325", "RT | RUST CLAN", 94, -1),
        };
    }
}
