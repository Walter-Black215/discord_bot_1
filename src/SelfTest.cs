using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DiscordAnalyzer
{
    // Проверка полного разбора на выдуманных данных (без интернета и без настоящего бота)
    public class FakeClient : DiscordClient
    {
        readonly List<KeyValuePair<string, string>> map;

        public FakeClient(List<KeyValuePair<string, string>> m) : base("test") { map = m; }

        public override object Get(string path)
        {
            foreach (var kv in map)
            {
                if (!path.StartsWith(kv.Key, StringComparison.Ordinal)) continue;
                if (kv.Value == "403") throw new ApiException(403, 50013, "Missing Permissions");
                return Json.Parse(kv.Value);
            }
            throw new ApiException(404, 0, "fake: " + path);
        }
    }

    public static class SelfTest
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Sf(double daysAgo)
        {
            var t = DateTime.UtcNow.AddDays(-daysAgo);
            long ms = (long)(t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return ((ulong)(ms - 1420070400000L) << 22).ToString(Inv);
        }

        static string Iso(double daysAgo)
        {
            return DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyy-MM-ddTHH:mm:ss.fff", Inv) + "+00:00";
        }

        static int idc = 100;

        static string Ch(string name, int type, string parent, int pos, double? lastDaysAgo)
        {
            idc++;
            return "{\"id\":\"" + idc + "\",\"name\":\"" + name + "\",\"type\":" + type + ",\"position\":" + pos +
                   ",\"parent_id\":" + (parent == null ? "null" : "\"" + parent + "\"") +
                   ",\"last_message_id\":" + (lastDaysAgo.HasValue ? "\"" + Sf(lastDaysAgo.Value) + "\"" : "null") + "}";
        }

        public static Report Run()
        {
            string gid = Sf(220);
            var chans = new List<string>();
            chans.Add("{\"id\":\"c1\",\"name\":\"👋 WELCOME\",\"type\":4,\"position\":0}");
            chans.Add("{\"id\":\"c2\",\"name\":\"🎫 RECRUITMENT\",\"type\":4,\"position\":1}");
            chans.Add("{\"id\":\"c3\",\"name\":\"💬 COMMUNITY\",\"type\":4,\"position\":2}");
            chans.Add("{\"id\":\"c4\",\"name\":\"🤝 PARTNERS\",\"type\":4,\"position\":3}");
            chans.Add("{\"id\":\"c5\",\"name\":\"🔊 VOICE\",\"type\":4,\"position\":4}");
            chans.Add(Ch("👋・ᴡᴇʟᴄᴏᴍᴇ", 0, "c1", 0, 1));
            chans.Add(Ch("📜・𝐫𝐮𝐥𝐞𝐬", 0, "c1", 1, 90));
            chans.Add(Ch("📢・news", 5, "c1", 2, 3));
            chans.Add(Ch("🏆・wipe-progression", 0, "c1", 3, null));
            chans.Add(Ch("📋・ticket-info", 0, "c2", 0, 40));
            chans.Add(Ch("🎫・ticket", 0, "c2", 1, 40));
            chans.Add(Ch("⏳ waiting", 2, "c2", 2, null));
            chans.Add(Ch("🔍 check 1", 2, "c2", 3, null));
            chans.Add(Ch("💬・ru-chat", 0, "c3", 0, 0.1));
            chans.Add(Ch("🌍・eu-chat", 0, "c3", 1, 45));
            chans.Add(Ch("🔎・lfg", 0, "c3", 2, 2));
            chans.Add(Ch("🎬・clips", 0, "c3", 3, 12));
            for (int i = 0; i < 12; i++) chans.Add(Ch("🤝・partner-" + (i + 1), 0, "c4", i, 20 + i));
            chans.Add(Ch("🔊 main", 2, "c5", 0, null));
            chans.Add(Ch("🔊 max 2", 2, "c5", 1, null));

            string roles =
                "[{\"id\":\"" + gid + "\",\"name\":\"@everyone\",\"position\":0,\"permissions\":\"134144\",\"hoist\":false}," +
                "{\"id\":\"r1\",\"name\":\"👑 ᴏᴡɴᴇʀ\",\"position\":10,\"permissions\":\"8\",\"hoist\":true}," +
                "{\"id\":\"r2\",\"name\":\"Admin\",\"position\":9,\"permissions\":\"0\",\"hoist\":true}," +
                "{\"id\":\"r3\",\"name\":\"Ticket Staff\",\"position\":8,\"permissions\":\"0\",\"hoist\":true}," +
                "{\"id\":\"r4\",\"name\":\"MAIN\",\"position\":7,\"permissions\":\"0\",\"hoist\":true}," +
                "{\"id\":\"r5\",\"name\":\"Member\",\"position\":2,\"permissions\":\"0\",\"hoist\":true}," +
                "{\"id\":\"r6\",\"name\":\"Server Booster\",\"position\":3,\"permissions\":\"0\",\"hoist\":false,\"tags\":{\"premium_subscriber\":null}}," +
                "{\"id\":\"r7\",\"name\":\"Ticket Tool\",\"position\":6,\"permissions\":\"0\",\"managed\":true,\"tags\":{\"bot_id\":\"557628352828014614\"}}," +
                "{\"id\":\"r8\",\"name\":\"Carl-bot\",\"position\":5,\"permissions\":\"0\",\"managed\":true,\"tags\":{\"bot_id\":\"235148962103951360\"}}]";

            string guild = "{\"id\":\"" + gid + "\",\"name\":\"𝐓𝐄𝐒𝐓 𝐂𝐋𝐀𝐍 | COMMUNITY\",\"description\":null,\"icon\":null," +
                           "\"features\":[\"COMMUNITY\"],\"premium_subscription_count\":3,\"verification_level\":1," +
                           "\"approximate_member_count\":640,\"approximate_presence_count\":150,\"default_message_notifications\":0," +
                           "\"explicit_content_filter\":0,\"rules_channel_id\":null,\"emojis\":[],\"roles\":" + roles + "}";

            var members = new StringBuilder("[");
            for (int i = 0; i < 40; i++)
            {
                if (i > 0) members.Append(",");
                bool bot = i >= 38;
                string rl = i == 0 ? "[\"r1\"]" : i < 3 ? "[\"r2\"]" : i < 9 ? "[\"r4\",\"r5\"]" : i < 25 ? "[\"r5\"]" : "[]";
                members.Append("{\"user\":{\"id\":\"" + (5000 + i) + "\",\"username\":\"" + (bot ? (i == 38 ? "Ticket Tool" : "Carl-bot") : "user" + i) + "\"" +
                               (bot ? ",\"bot\":true" : "") + "},\"roles\":" + rl + ",\"joined_at\":\"" + Iso(i * 2.3) + "\"}");
            }
            members.Append("]");

            var msgs = new StringBuilder("[");
            for (int i = 0; i < 60; i++)
            {
                if (i > 0) msgs.Append(",");
                msgs.Append("{\"id\":\"" + (9000 + i) + "\",\"timestamp\":\"" + Iso(i * 0.15) + "\",\"author\":{\"id\":\"" + (5000 + i % 14) + "\"}}");
            }
            msgs.Append("]");

            string invites =
                "[{\"code\":\"tiktok1\",\"uses\":120,\"max_age\":0,\"inviter\":{\"username\":\"owner\"},\"channel\":{\"name\":\"welcome\"}}," +
                "{\"code\":\"partnerA\",\"uses\":35,\"max_age\":604800,\"inviter\":{\"username\":\"admin\"},\"channel\":{\"name\":\"welcome\"}}]";

            var map = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("/guilds/" + gid + "?", guild),
                new KeyValuePair<string, string>("/guilds/" + gid + "/channels", "[" + string.Join(",", chans) + "]"),
                new KeyValuePair<string, string>("/guilds/" + gid + "/members?limit=1000&after=0", members.ToString()),
                new KeyValuePair<string, string>("/guilds/" + gid + "/members", "[]"),
                new KeyValuePair<string, string>("/guilds/" + gid + "/invites", invites),
                new KeyValuePair<string, string>("/channels/", msgs.ToString()),
            };
            return Analyzer.AnalyzeBot(new FakeClient(map), gid, s => { }, false);
        }
    }
}
