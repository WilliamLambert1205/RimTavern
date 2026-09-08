using System.Collections.Generic;
using System.Text;
using Verse;

namespace RimTavern.Logic
{
    /// <summary>
    /// Main-thread helpers that turn a live Pawn into a short localized description
    /// used in prompts. Never call these from a worker thread.
    /// </summary>
    public static class PawnText
    {
        public static string Describe(Pawn p)
        {
            if (p == null) return "？";
            var sb = new StringBuilder();
            sb.Append(p.LabelCap);
            if (p.ageTracker != null)
            {
                sb.Append("，").Append(p.ageTracker.AgeBiologicalYears).Append("岁");
            }
            if (p.gender != Gender.None)
            {
                sb.Append("，").Append(p.gender == Gender.Male ? "男" : (p.gender == Gender.Female ? "女" : "其他"));
            }
            // Title/role (殖民者职业头衔、或敌方头衔等)
            string title = p.story?.Title;
            if (!string.IsNullOrEmpty(title))
            {
                sb.Append("，职位是").Append(title);
            }
            // Traits
            var traits = p.story?.traits?.allTraits;
            if (traits != null && traits.Count > 0)
            {
                var list = new List<string>();
                foreach (var t in traits)
                {
                    if (t?.def != null) list.Add(t.def.LabelCap);
                }
                if (list.Count > 0) sb.Append("，性格特质：").Append(string.Join("、", list));
            }
            // Mood
            float? mood = p.needs?.mood?.CurLevelPercentage;
            if (mood.HasValue)
            {
                string m;
                if (mood.Value < 0.2f) m = "非常糟糕";
                else if (mood.Value < 0.4f) m = "很差";
                else if (mood.Value < 0.6f) m = "一般";
                else if (mood.Value < 0.85f) m = "不错";
                else m = "很好";
                sb.Append("，此刻心情").Append(m);
            }
            // Current activity (label omitted on purpose: exact job defs are an implementation detail)
            sb.Append("，此刻").Append(p.jobs?.curDriver != null ? "正忙着" : "空闲");
            string result = StripRichText(sb.ToString());
            // collapse artifacts like empty list separators left by empty trait labels
            result = result.Replace("、、", "、").Replace("，、", "").Replace("、、", "");
            return result;
        }

        /// <summary>
        /// RimWorld game labels frequently embed Unity rich-text markup
        /// (<color=#999999FF>…</color>, &lt;b&gt;…&lt;/b&gt;). That markup must never reach the LLM.
        /// </summary>
        public static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<')
                {
                    int end = s.IndexOf('>', i + 1);
                    if (end < 0) { sb.Append(c); continue; }
                    i = end; // skip the whole tag
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
