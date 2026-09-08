using System;
using System.Collections.Generic;
using System.Text;

namespace RimTavern.Util
{
    public class DiagEntry
    {
        public string Time;
        public string Section;
        public string Text;
    }

    /// <summary>
    /// Central diagnostics log (design evolves with development, see DEVELOPMENT_NOTES 十一节).
    /// Records BOTH what was uploaded/injected to the LLM (full prompts: system/user/history,
    /// scene block, character-card block, lore block) and what came back (raw model text, parse
    /// results, errors). Bounded in-memory ring buffer; "copy" exports a formatted snapshot.
    /// </summary>
    public static class Diag
    {
        public const string LogVersion = "v1";

        private static readonly List<DiagEntry> Buffer = new List<DiagEntry>();
        private const int MaxEntries = 150;

        public static bool Enabled
        {
            get
            {
                try { return RimTavernMod.Settings != null && RimTavernMod.Settings.logDiagnostics; }
                catch { return false; }
            }
        }

        public static int Count { get { return Buffer.Count; } }

        public static void Log(string section, string text)
        {
            if (!Enabled) return;
            if (string.IsNullOrEmpty(text)) return;
            Buffer.Add(new DiagEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Section = section,
                Text = text
            });
            if (Buffer.Count > MaxEntries)
            {
                Buffer.RemoveRange(0, Buffer.Count - MaxEntries);
            }
        }

        public static void Clear()
        {
            Buffer.Clear();
        }

        /// <summary>
        /// Export a formatted snapshot for the copy button. Covers everything in the ring buffer,
        /// newest last; big snapshot truncated at maxChars keeping the tail (most recent).
        /// </summary>
        public static string FormatRecent(int maxChars = 60000)
        {
            var sb = new StringBuilder();
            sb.Append("==== RimTavern 调试日志 ").Append(LogVersion)
              .Append(" · ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine(" ====");
            sb.AppendLine("含义：req|=提交给模型的内容（含注入的卡/世界书/场景/历史）；resp|=模型原文返回；");
            sb.AppendLine("      parse|=解析结果；scene|=场景重建内容；inject|=场景注入事件；error|=错误。");
            if (Buffer.Count == 0)
            {
                sb.AppendLine("（空：请先在设置开启“日志诊断输出”，再触发一次对话/选项。）");
                return sb.ToString();
            }
            sb.AppendLine("----");
            foreach (DiagEntry e in Buffer)
            {
                sb.Append('[').Append(e.Time).Append("] ").Append(e.Section).AppendLine();
                sb.AppendLine(e.Text);
                sb.AppendLine("- - -");
            }
            if (sb.Length > maxChars)
            {
                // keep the tail (most recent context) but still head of the buffer inside budget
                return sb.ToString().Substring(Math.Max(0, sb.Length - maxChars));
            }
            return sb.ToString();
        }
    }
}
