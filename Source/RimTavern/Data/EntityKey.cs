using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimTavern.Data
{
    /// <summary>
    /// P1-2: stable textual keys for runtime entities, plus reverse resolution ("name + recency" is
    /// handled by callers; this class gives the deterministic id scheme and direct resolvers).
    /// Keys are engine-internal and NEVER produced by the LLM.
    /// </summary>
    public static class EntityKey
    {
        public const string PawnPrefix = "pawn:";
        public const string FactionPrefix = "faction:";
        public const string ThingDefPrefix = "def:";
        public const string QuestPrefix = "quest:";
        public const string WorldObjectPrefix = "wo:";

        public static string ForPawn(Pawn p)
        {
            return p == null ? null : PawnPrefix + p.thingIDNumber;
        }

        public static string ForFaction(Faction f)
        {
            return f == null ? null : FactionPrefix + f.def.defName + ":" + f.loadID;
        }

        public static string ForDef(Def d)
        {
            return d == null ? null : ThingDefPrefix + d.defName;
        }

        public static string ForQuest(Quest q)
        {
            return q == null ? null : QuestPrefix + q.id;
        }

        public static Pawn ResolvePawn(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith(PawnPrefix)) return null;
            if (!int.TryParse(key.Substring(PawnPrefix.Length), out int id)) return null;
            if (Find.Maps == null) return null;
            for (int m = 0; m < Find.Maps.Count; m++)
            {
                List<Pawn> all = Find.Maps[m].mapPawns.AllPawns;
                if (all == null) continue;
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p != null && p.thingIDNumber == id) return p;
                }
            }
            return null;
        }

        public static Faction ResolveFaction(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith(FactionPrefix) || Find.FactionManager == null) return null;
            string rest = key.Substring(FactionPrefix.Length);
            int colon = rest.IndexOf(':');
            string defName = colon >= 0 ? rest.Substring(0, colon) : rest;
            foreach (Faction f in Find.FactionManager.AllFactions)
            {
                if (f != null && f.def != null && f.def.defName == defName) return f;
            }
            return null;
        }
    }
}
