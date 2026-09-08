using Verse;

namespace RimTavern.Data
{
    /// <summary>P4-1: memory kinds for the structured story memory.</summary>
    public enum MemKind
    {
        Chat = 0,     // what happened / was said (post-dialogue summary)
        Promise = 1,  // a promise/commitment between protagonist and an NPC
        Grudge = 2,
        Secret = 3,
        Event = 4,
        Note = 5
    }

    /// <summary>
    /// P4-1: one structured memory record, persisted with the save game.
    /// npcId = -1 means a colony/global memory not tied to one NPC.
    /// </summary>
    public class MemoryRecord : IExposable
    {
        public int kind = (int)MemKind.Chat;
        public int tick;
        public int npcId = -1;
        public int importance = 1;
        public string summary = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind", (int)MemKind.Chat, false);
            Scribe_Values.Look(ref tick, "tick", 0, false);
            Scribe_Values.Look(ref npcId, "npcId", -1, false);
            Scribe_Values.Look(ref importance, "importance", 1, false);
            Scribe_Values.Look(ref summary, "summary", "", false);
        }

        public static string KindLabel(int k)
        {
            switch ((MemKind)k)
            {
                case MemKind.Promise: return "承诺";
                case MemKind.Grudge: return "恩怨";
                case MemKind.Secret: return "秘密";
                case MemKind.Event: return "事件";
                case MemKind.Note: return "备注";
                default: return "过往";
            }
        }
    }

    /// <summary>
    /// P4-2: result of the dialogue-end wrap-up LLM call (engine-validated small deltas only).
    /// </summary>
    public class MemoryOutcome
    {
        public int npcId;
        public int affinityDelta;
        public string summary = "";
    }
}
