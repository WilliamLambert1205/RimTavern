using System.Collections.Generic;
using System.Text;
using RimTavern.Util;

namespace RimTavern.Data
{
    /// <summary>
    /// P1-6/P3: one worldbook ("lore") entry. Schema is deliberately a subset of SillyTavern
    /// world_info entries (constant + keyword activation); UI-only fields from ST
    /// (probability / selective logic / vectorized / depth / weight / characterFilter / position)
    /// are intentionally dropped on import.
    /// </summary>
    public class LoreEntry
    {
        public string Uid = "";
        public string Title = "";
        public string Content = "";
        public string Comment = "";
        public bool Constant;
        public bool Enabled = true;
        public List<string> Keys = new List<string>();
        public string Source = ""; // file path or "in-game"

        public string KeySummary
        {
            get { return Keys.Count == 0 ? "" : string.Join("、", Keys); }
        }
    }

    public static class LoreEntryIO
    {
        /// <summary>Parse a SillyTavern world_info JSON export (entries may be an array or a uid-keyed object).</summary>
        public static List<LoreEntry> ParseWorldInfoJson(string json, string source)
        {
            var result = new List<LoreEntry>();
            JsonNode root = Json.Parse(json);
            JsonNode entries = root?.Get("entries");
            if (entries == null) return result;

            List<JsonNode> nodes = new List<JsonNode>();
            if (entries is JsonArray arr)
            {
                nodes.AddRange(arr.Items);
            }
            else if (entries is JsonObject obj)
            {
                foreach (var kv in obj.Fields) nodes.Add(kv.Value);
            }

            foreach (JsonNode n in nodes)
            {
                LoreEntry e = ParseOne(n);
                if (string.IsNullOrEmpty(e.Content) && e.Keys.Count == 0) continue;
                e.Source = source;
                if (string.IsNullOrEmpty(e.Uid) && !string.IsNullOrEmpty(source))
                {
                    e.Uid = e.Title + "_" + result.Count;
                }
                result.Add(e);
            }
            return result;
        }

        private static LoreEntry ParseOne(JsonNode n)
        {
            var e = new LoreEntry();
            if (n == null) return e;
            e.Uid = n.GetStr("uid", "");
            if (string.IsNullOrEmpty(e.Uid)) e.Uid = n.GetStr("id", "");
            e.Title = n.GetStr("name", "");
            if (string.IsNullOrEmpty(e.Title)) e.Title = n.GetStr("title", "");
            e.Comment = n.GetStr("comment", "");
            e.Content = n.GetStr("content", "");
            e.Constant = n.GetBool("constant", false);
            if (n.GetBool("disable", false)) e.Enabled = false;
            else if (n.Get("enabled") != null) e.Enabled = n.GetBool("enabled", true);

            // keys: newer ST uses array "keys"; older exports use comma string "key"
            JsonArray keysArr = n.GetArray("keys");
            if (keysArr != null)
            {
                foreach (JsonNode k in keysArr.Items)
                {
                    string s = JsonNode.Str(k);
                    AddKey(e, s);
                }
            }
            AddKey(e, n.GetStr("key", ""));
            AddKey(e, n.GetStr("keysecondary", ""));
            return e;
        }

        private static void AddKey(LoreEntry e, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            foreach (string part in raw.Split(','))
            {
                string k = part.Trim();
                if (k.Length > 0 && !e.Keys.Contains(k)) e.Keys.Add(k);
            }
        }

        /// <summary>Serialize our subset back to a small world_info JSON (entries array).</summary>
        public static string ToJson(List<LoreEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("{\"name\":\"RimTavern worldbook\",\"entries\":[");
            bool first = true;
            foreach (LoreEntry e in entries)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                sb.Append("\"uid\":\"").Append(Json.Escape(e.Uid)).Append("\",");
                sb.Append("\"name\":\"").Append(Json.Escape(e.Title)).Append("\",");
                sb.Append("\"comment\":\"").Append(Json.Escape(e.Comment)).Append("\",");
                sb.Append("\"content\":\"").Append(Json.Escape(e.Content)).Append("\",");
                sb.Append("\"constant\":").Append(e.Constant ? "true" : "false").Append(',');
                sb.Append("\"enabled\":").Append(e.Enabled ? "true" : "false").Append(',');
                sb.Append("\"keys\":[");
                bool kFirst = true;
                foreach (string k in e.Keys)
                {
                    if (!kFirst) sb.Append(',');
                    kFirst = false;
                    sb.Append('"').Append(Json.Escape(k)).Append('"');
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
