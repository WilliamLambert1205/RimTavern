using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RimTavern.Util
{
    /// <summary>
    /// Minimal, dependency-free JSON parser/writer.
    /// RimWorld ships no Newtonsoft.Json and we do not want to deploy a conflicting dll,
    /// so we roll our own small parser (recursive descent) tolerant to LLM output quirks
    /// (markdown fences, trailing commas, control characters).
    /// Adapted from the author's previous project AICharacterGen (Util/Json.cs).
    /// </summary>
    public abstract class JsonNode
    {
        public abstract string ToJson();

        public static string Str(JsonNode n, string fallback = null)
        {
            return n is JsonString s ? s.Value : fallback;
        }

        public static double Num(JsonNode n, double fallback = 0.0)
        {
            return n is JsonNumber d ? d.Value : fallback;
        }

        public static bool Bool(JsonNode n, bool fallback = false)
        {
            return n is JsonBool b ? b.Value : fallback;
        }

        public JsonNode Get(string key)
        {
            return this is JsonObject o && o.TryGetValue(key, out var v) ? v : null;
        }

        public string GetStr(string key, string fallback = null)
        {
            return Str(Get(key), fallback);
        }

        public double GetNum(string key, double fallback = 0.0)
        {
            return Num(Get(key), fallback);
        }

        public bool GetBool(string key, bool fallback = false)
        {
            return Bool(Get(key), fallback);
        }

        public JsonArray GetArray(string key)
        {
            return Get(key) as JsonArray;
        }
    }

    public sealed class JsonObject : JsonNode
    {
        public readonly Dictionary<string, JsonNode> Fields = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        public bool TryGetValue(string key, out JsonNode value)
        {
            return Fields.TryGetValue(key, out value);
        }

        public override string ToJson()
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in Fields)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(Json.Escape(kv.Key)).Append("\":").Append(kv.Value.ToJson());
            }
            sb.Append('}');
            return sb.ToString();
        }
    }

    public sealed class JsonArray : JsonNode
    {
        public readonly List<JsonNode> Items = new List<JsonNode>();

        public override string ToJson()
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var item in Items)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(item.ToJson());
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    public sealed class JsonString : JsonNode
    {
        public readonly string Value;

        public JsonString(string value) { Value = value ?? ""; }

        public override string ToJson()
        {
            return "\"" + Json.Escape(Value) + "\"";
        }
    }

    public sealed class JsonNumber : JsonNode
    {
        public readonly double Value;

        public JsonNumber(double value) { Value = value; }

        public override string ToJson()
        {
            if (double.IsNaN(Value) || double.IsInfinity(Value)) return "0";
            return Value.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    public sealed class JsonBool : JsonNode
    {
        public readonly bool Value;

        public JsonBool(bool value) { Value = value; }

        public override string ToJson() { return Value ? "true" : "false"; }
    }

    public sealed class JsonNull : JsonNode
    {
        public static readonly JsonNull Instance = new JsonNull();
        private JsonNull() { }
        public override string ToJson() { return "null"; }
    }

    public static class Json
    {
        /// <summary>Parse JSON, tolerating LLM quirks (fenced code blocks, trailing commas, BOM). Returns null on failure.</summary>
        public static JsonNode Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            // Strip markdown code fences if present.
            text = text.Trim();
            if (text.StartsWith("```"))
            {
                int nl = text.IndexOf('\n');
                if (nl >= 0) text = text.Substring(nl + 1);
                int end = text.LastIndexOf("```", StringComparison.Ordinal);
                if (end >= 0) text = text.Substring(0, end);
                text = text.Trim();
            }
            int pos = 0;
            try
            {
                JsonNode node = ParseValue(text, ref pos);
                return node;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Parse the next top-level JSON value starting at <paramref name="pos"/>.
        /// Advances pos past it. Throws FormatException if there is no valid value at pos.
        /// </summary>
        public static JsonNode ParseNext(string text, ref int pos)
        {
            JsonNode node = ParseValue(text, ref pos);
            return node;
        }

        private static JsonNode ParseValue(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length) throw new FormatException("unexpected end");
            char c = s[pos];
            switch (c)
            {
                case '{': return ParseObject(s, ref pos);
                case '[': return ParseArray(s, ref pos);
                case '"': return new JsonString(ParseString(s, ref pos));
                case 't': Expect(s, ref pos, "true"); return new JsonBool(true);
                case 'f': Expect(s, ref pos, "false"); return new JsonBool(false);
                case 'n': Expect(s, ref pos, "null"); return JsonNull.Instance;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref pos);
                    throw new FormatException("unexpected char " + c);
            }
        }

        private static JsonObject ParseObject(string s, ref int pos)
        {
            var obj = new JsonObject();
            pos++; // {
            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; return obj; }
            while (true)
            {
                SkipWs(s, ref pos);
                if (pos >= s.Length) throw new FormatException("object unterminated");
                if (s[pos] != '"') throw new FormatException("expected key string");
                string key = ParseString(s, ref pos);
                SkipWs(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') throw new FormatException("expected ':'");
                pos++;
                JsonNode value = ParseValue(s, ref pos);
                if (obj.Fields.ContainsKey(key)) obj.Fields[key] = value;
                else obj.Fields.Add(key, value);
                SkipWs(s, ref pos);
                if (pos >= s.Length) throw new FormatException("object unterminated");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return obj; }
                throw new FormatException("expected ',' or '}'");
            }
        }

        private static JsonArray ParseArray(string s, ref int pos)
        {
            var arr = new JsonArray();
            pos++; // [
            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; return arr; }
            while (true)
            {
                arr.Items.Add(ParseValue(s, ref pos));
                SkipWs(s, ref pos);
                if (pos >= s.Length) throw new FormatException("array unterminated");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return arr; }
                throw new FormatException("expected ',' or ']'");
            }
        }

        private static string ParseString(string s, ref int pos)
        {
            pos++; // opening quote
            var sb = new StringBuilder();
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '"') { pos++; return sb.ToString(); }
                if (c == '\\')
                {
                    pos++;
                    if (pos >= s.Length) break;
                    char e = s[pos];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 < s.Length &&
                                int.TryParse(s.Substring(pos + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                            {
                                sb.Append((char)cp);
                                pos += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                    pos++;
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }
            throw new FormatException("string unterminated");
        }

        private static JsonNumber ParseNumber(string s, ref int pos)
        {
            int start = pos;
            if (pos < s.Length && s[pos] == '-') pos++;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E' || s[pos] == '+' || s[pos] == '-'))
                pos++;
            string token = s.Substring(start, pos - start);
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return new JsonNumber(d);
            throw new FormatException("bad number " + token);
        }

        private static void Expect(string s, ref int pos, string word)
        {
            if (pos + word.Length > s.Length || string.CompareOrdinal(s, pos, word, 0, word.Length) != 0)
                throw new FormatException("expected " + word);
            pos += word.Length;
        }

        private static void SkipWs(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
