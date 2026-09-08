using System.Collections.Generic;
using Verse;

namespace RimTavern.UI
{
    /// <summary>One line shown in the history overlay / window.</summary>
    public class OvlLine
    {
        public string speaker;
        public string text;
        public bool isNpc;
    }

    /// <summary>
    /// Shared recent-lines buffer for the overlay. Drawing is done by TavernChatLogWindow
    /// (a RimWorld Window), which is guaranteed to render - no map OnGUI hooks involved.
    /// </summary>
    public static class TavernHistory
    {
        public static readonly List<OvlLine> Ring = new List<OvlLine>();

        public static void Push(string speaker, string text, bool isNpc)
        {
            if (string.IsNullOrEmpty(text)) return;
            Ring.Add(new OvlLine { speaker = speaker, text = text, isNpc = isNpc });
            while (Ring.Count > 60) Ring.RemoveAt(0);
        }

        public static void Clear()
        {
            Ring.Clear();
        }

        public static void SetFromArchive(int max)
        {
            var comp = RimTavern.Core.RimTavernGameComp.Get();
            if (comp == null) return;
            var lines = comp.GetLastDialogueLines(max);
            Ring.Clear();
            foreach (var l in lines)
            {
                Ring.Add(new OvlLine { speaker = l.speaker, text = l.text, isNpc = l.isNpc });
            }
        }
    }
}
