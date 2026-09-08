using System.Text;
using RimTavern.Logic;
using RimTavern.Util;
using Verse;

namespace RimTavern.Data
{
    /// <summary>
    /// P1-5: author-authored character card (simplified subset of chara_card_v2).
    /// This is ONLY the author override layer. Runtime auto data (mood/job/… PawnText.Describe) is
    /// merged later by the prompt builder; it is never persisted here, so an authored card is never
    /// overwritten by auto defaults. Empty fields simply mean "use the auto part".
    /// </summary>
    public class CharacterCard
    {
        public string Uid = "";          // stable id: "user:"+nameKey or "file:"+fileName (P1 assignment)
        public string Name = "";         // matched pawn full/short label (author layer target name)
        public string Personality = "";  // 性格要点 (chara description/personality)
        public string SpeechExamples = "";  // 说话范例 (chara mes_example)
        public string Scenario = "";        // 附加设定 (chara scenario, optional)
        public string SourceFile = "";
        public bool IsUserOwned;         // runtime flag: true = written by this mod (_card_*), false = ST-imported

        public string OriginLabel
        {
            get { return IsUserOwned ? "本 Mod 编辑" : "ST 导入"; }
        }

        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrEmpty(Personality) && string.IsNullOrEmpty(SpeechExamples)
                    && string.IsNullOrEmpty(Scenario);
            }
        }

        /// <summary>Normalized lookup key (lowercased full name, falls back to label).</summary>
        public static string NormalizeName(string s)
        {
            return s == null ? "" : s.Trim().ToLowerInvariant();
        }

        /// <summary>Write a chara_card_v2 JSON (ST-compatible subset) for file export / reuse.</summary>
        public string ToCharaV2Json()
        {
            var sb = new StringBuilder();
            sb.Append("{\"spec\":\"chara_card_v2\",\"spec_version\":\"2.0\",\"data\":{");
            sb.Append("\"name\":\"").Append(Json.Escape(Name)).Append("\",");
            sb.Append("\"description\":\"").Append(Json.Escape(Personality)).Append("\",");
            sb.Append("\"personality\":\"\",");
            sb.Append("\"scenario\":\"").Append(Json.Escape(Scenario)).Append("\",");
            sb.Append("\"first_mes\":\"\",");
            sb.Append("\"mes_example\":\"").Append(Json.Escape(SpeechExamples)).Append("\"");
            sb.Append("}}");
            return sb.ToString();
        }

        /// <summary>Parse a chara_card_v2 JSON (ST import or our own export).</summary>
        public static CharacterCard ParseCharaV2(string json)
        {
            var card = new CharacterCard();
            JsonNode root = Json.Parse(json);
            JsonNode data = root?.Get("data") ?? root;
            if (data == null) return card;
            card.Name = data.GetStr("name", "");
            // ST cards pack appearance+personality into description; keep it in one editable block.
            string desc = data.GetStr("description", "");
            string pers = data.GetStr("personality", "");
            card.Personality = JoinNonEmpty(desc, pers);
            card.Scenario = data.GetStr("scenario", "");
            card.SpeechExamples = data.GetStr("mes_example", "");
            return card;
        }

        /// <summary>
        /// Auto default card used ONLY for runtime prompt context when no authored card exists.
        /// Never persisted, never written to disk.
        /// </summary>
        public static CharacterCard BuildAuto(Pawn p)
        {
            var card = new CharacterCard();
            if (p == null) return card;
            card.Name = p.Name != null ? p.Name.ToStringFull : p.LabelShort;
            card.Personality = PawnText.Describe(p);
            return card;
        }

        private static string JoinNonEmpty(string a, string b)
        {
            string ra = (a ?? "").Trim();
            string rb = (b ?? "").Trim();
            if (ra.Length == 0) return rb;
            if (rb.Length == 0) return ra;
            return ra + "\n" + rb;
        }
    }
}
