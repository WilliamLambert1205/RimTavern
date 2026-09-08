using System;
using System.Collections.Generic;
using System.Text;
using RimTavern.Core;
using RimTavern.Data;
using Verse;

namespace RimTavern.Logic
{
    /// <summary>
    /// Main-thread context assembly for prompts (P1.5 cards + P3 lore activation).
    /// Merges the runtime auto layer (PawnText.Describe) with the authored override layer
    /// (effective card = save-game binding -> legacy name match -> auto). Auto content is
    /// computed live and never persisted, so authored edits are never overwritten.
    /// </summary>
    public static class ContextAssembler
    {
        /// <summary>Resolve the effective authored card for a pawn (binding first, name fallback).</summary>
        public static CharacterCard ResolveCard(Pawn p)
        {
            if (p == null) return null;
            var comp = RimTavernGameComp.Get();
            if (comp != null)
            {
                string uid = comp.GetBoundCardUid(p.thingIDNumber);
                if (uid != null)
                {
                    CharacterCard c = ContentStore.GetCard(uid);
                    if (c != null) return c;
                }
            }
            return ContentStore.FindForPawn(p);
        }

        /// <summary>
        /// Human-readable identity + dynamic + authored block for one speaker.
        /// includeStyle adds speech-example guidance (used for the NPC who must sound in-character).
        /// </summary>
        public static string SpeakerBlock(Pawn p, string roleName, bool includeStyle, int maxChars = 600)
        {
            if (p == null) return "";
            CharacterCard eff = ResolveCard(p);
            var sb = new StringBuilder();
            string dynamicLine = PawnText.Describe(p);
            sb.Append(roleName).Append("现状：").Append(dynamicLine);
            if (eff != null)
            {
                if (!string.IsNullOrEmpty(eff.Personality))
                {
                    sb.Append("\n").Append(roleName).Append("固定性格（作者设定，优先级最高）：").Append(eff.Personality);
                }
                if (!string.IsNullOrEmpty(eff.Scenario))
                {
                    sb.Append("\n").Append(roleName).Append("背景补遗：").Append(eff.Scenario);
                }
                if (includeStyle && !string.IsNullOrEmpty(eff.SpeechExamples))
                {
                    sb.Append("\n").Append(roleName).Append("说话风格示范（只用于感受语气与用词习惯，严禁逐字照抄下列句子，说出内容时必须换成自己的话）：").Append(eff.SpeechExamples);
                }
            }
            string s = sb.ToString();
            return s.Length > maxChars ? s.Substring(0, maxChars) + "…" : s;
        }

        /// <summary>
        /// P3: activate worldbook entries (constant always; keyword entries when any key occurs in
        /// the search text), then format within a budget. Deterministic, no LLM.
        /// Guarantees: never emits an empty 【相关背景】 header (long entries are truncated to fit),
        /// and logs which entries hit / why none did (see DEVELOPMENT_NOTES 十一).
        /// </summary>
        public static string ActiveLoreBlock(string searchText, int maxChars)
        {
            if (string.IsNullOrEmpty(searchText)) return "";
            string hay = " " + searchText.ToLowerInvariant() + " ";
            var chosen = new List<LoreEntry>();
            foreach (LoreEntry e in ContentStore.Lore)
            {
                if (e == null || !e.Enabled || string.IsNullOrEmpty(e.Content)) continue;
                if (e.Constant)
                {
                    chosen.Add(e);
                    continue;
                }
                foreach (string k in e.Keys)
                {
                    if (string.IsNullOrEmpty(k)) continue;
                    string key = k.ToLowerInvariant().Trim();
                    if (key.Length == 0) continue;
                    if (hay.Contains(key))
                    {
                        chosen.Add(e);
                        break;
                    }
                }
            }

            if (chosen.Count == 0)
            {
                if (RimTavern.Util.Diag.Enabled)
                {
                    string sample = searchText.Replace("\n", " ").Trim();
                    RimTavern.Util.Diag.Log("lore|miss",
                        "无命中。搜索样本（前160字）：" + (sample.Length > 160 ? sample.Substring(0, 160) + "…" : sample));
                }
                return "";
            }

            // dedupe (same content may exist as imported file + in-game user entry)
            var entries = new List<LoreEntry>();
            var seenContent = new HashSet<string>(StringComparer.Ordinal);
            foreach (LoreEntry e in chosen)
            {
                string k = (e.Title ?? "") + "|" + (e.Content ?? "");
                if (seenContent.Add(k)) entries.Add(e);
            }

            var sb = new StringBuilder();
            sb.Append("【相关背景】");
            int budget = maxChars > 0 ? maxChars : 600;
            int used = sb.Length;
            bool anyAppended = false;
            var hitNames = new List<string>();
            foreach (LoreEntry e in entries)
            {
                hitNames.Add(e.Title.Length > 0 ? e.Title : (e.Keys.Count > 0 ? e.Keys[0] : e.Uid));
                string head = e.Title.Length > 0 ? "·" + e.Title + "：" : "·";
                string content = e.Content.Replace("\r\n", " ").Replace("\n", " ").Trim();
                // keep the first ~2 sentences of very long entries to stay budget-friendly
                int line = head.Length + content.Length;
                int remain = budget - used;
                if (line + 2 > remain)
                {
                    if (remain <= 24) break;              // nothing left; we already have content
                    string tail = content;
                    if (tail.Length > remain - head.Length - 2)
                    {
                        tail = content.Substring(0, Math.Max(0, remain - head.Length - 2 - 3)) + "…";
                    }
                    sb.Append('\n').Append(head).Append(tail);
                    used += head.Length + tail.Length + 2;
                    anyAppended = true;
                    break;
                }
                sb.Append('\n').Append(head).Append(content);
                used += head.Length + content.Length + 2;
                anyAppended = true;
            }

            if (!anyAppended)
            {
                // extremely small budget: force a stub rather than emit an empty header
                if (budget > 20 && entries.Count > 0)
                {
                    LoreEntry first = entries[0];
                    sb.Append('\n').Append("·").Append(first.Title.Length > 0 ? first.Title + "：" : "");
                    return sb.ToString();
                }
                return "";
            }

            if (RimTavern.Util.Diag.Enabled)
            {
                RimTavern.Util.Diag.Log("lore|hit", "命中条目：" + string.Join(" / ", hitNames));
            }
            return sb.ToString();
        }
    }
}
