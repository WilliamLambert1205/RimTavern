using System.Collections.Generic;
using Verse;

namespace RimTavern.Data
{
    /// <summary>One dialogue line, archived with the save.</summary>
    public class SavedLine : IExposable
    {
        public string speaker = "";
        public string text = "";
        public bool isNpc;

        public void ExposeData()
        {
            Scribe_Values.Look(ref speaker, "speaker", "", false);
            Scribe_Values.Look(ref text, "text", "", false);
            Scribe_Values.Look(ref isNpc, "isNpc", false, false);
        }

        public SavedLine() { }

        public SavedLine(string speaker, string text, bool isNpc)
        {
            this.speaker = speaker;
            this.text = text;
            this.isNpc = isNpc;
        }
    }

    /// <summary>One finished dialogue session, persisted in the save game for replay/browsing.</summary>
    public class SavedDialogue : IExposable
    {
        public int tick;
        public string protoLabel = "";
        public string npcLabel = "";
        public List<SavedLine> lines = new List<SavedLine>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick", 0, false);
            Scribe_Values.Look(ref protoLabel, "protoLabel", "", false);
            Scribe_Values.Look(ref npcLabel, "npcLabel", "", false);
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
        }

        public SavedDialogue() { }

        public SavedDialogue(int tick, string protoLabel, string npcLabel, List<SavedLine> lines)
        {
            this.tick = tick;
            this.protoLabel = protoLabel;
            this.npcLabel = npcLabel;
            this.lines = lines;
        }
    }
}
