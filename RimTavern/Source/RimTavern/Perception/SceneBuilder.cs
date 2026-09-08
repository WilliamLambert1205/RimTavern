using System;
using System.Collections.Generic;
using System.Text;
using RimTavern.Core;
using RimWorld;
using Verse;

namespace RimTavern.Perception
{
    /// <summary>Query handed to every scene source for one context snapshot.</summary>
    public class SceneQuery
    {
        public Pawn Focus;
        public Pawn Other;
        public Map Map;
        public int BudgetChars = 900;
    }

    /// <summary>
    /// P2: generic scene provider. Each source reads RimWorld base abstractions only
    /// (Pawn/Thing/def/…), so any mod content registered in those abstractions flows in
    /// automatically — no per-content whitelists.
    /// </summary>
    public interface ISceneSource
    {
        string SourceName { get; }
        void Collect(SceneQuery q, List<SceneFact> into);
    }

    public static class SceneBuilder
    {
        private static readonly List<ISceneSource> Sources = new List<ISceneSource>
        {
            new ActorsSource(),
            new MapContentsSource(),
            new EquipmentSource(),
            new ContainerSource(),
            new WorldSource()
        };

        public static string GlossFor(Def d)
        {
            if (d == null) return "";
            string s = d.description;
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            if (nl > 0) s = s.Substring(0, nl);
            s = s.Trim();
            return s.Length > 110 ? s.Substring(0, 110) + "…" : s;
        }

        public static string GlossForShort(Def d, int max)
        {
            string s = GlossFor(d);
            if (s.Length > max) return s.Substring(0, max) + "…";
            return s;
        }

        /// <summary>Build the scene text block for a dialogue (main thread only).</summary>
        public static string BuildSceneBlock(RimTavernGameComp comp, Pawn focus, Pawn other)
        {
            try
            {
                if (focus == null || other == null) return "";
                Map map = focus.Map ?? other.Map;
                if (map == null) return "";

                var q = new SceneQuery { Focus = focus, Other = other, Map = map };
                q.BudgetChars = RimTavernMod.Settings != null && RimTavernMod.Settings.sceneBudgetChars > 0
                    ? RimTavernMod.Settings.sceneBudgetChars : 900;

                var facts = new List<SceneFact>();
                foreach (ISceneSource src in Sources)
                {
                    try { src.Collect(q, facts); }
                    catch (Exception ex) { Log.Warning("[RimTavern] SceneSource '" + src.SourceName + "' failed: " + ex.Message); }
                }

                return Format(Rank(q, facts));
            }
            catch (Exception ex)
            {
                Log.Warning("[RimTavern] SceneBuilder failed: " + ex.Message);
                return "";
            }
        }

        private static List<SceneFact> Rank(SceneQuery q, List<SceneFact> facts)
        {
            facts.Sort((a, b) => b.Salience.CompareTo(a.Salience));
            var result = new List<SceneFact>();
            int budget = q.BudgetChars;
            int used = 0;
            var seen = new HashSet<string>();
            var catCount = new Dictionary<SceneFactCategory, int>();
            var caps = new Dictionary<SceneFactCategory, int>
            {
                { SceneFactCategory.Actor, 8 },
                { SceneFactCategory.MapThing, 4 },
                { SceneFactCategory.Equipment, 3 },
                { SceneFactCategory.World, 2 }
            };
            for (int i = 0; i < facts.Count && used < budget && result.Count < 22; i++)
            {
                SceneFact f = facts[i];
                if (string.IsNullOrEmpty(f.Name)) continue;
                if (caps.TryGetValue(f.Category, out int cap))
                {
                    catCount.TryGetValue(f.Category, out int cur);
                    if (cur >= cap) continue;
                    catCount[f.Category] = cur + 1;
                }
                string key = f.Name + "|" + (f.Gloss ?? "");
                if (!seen.Add(key)) continue;
                int cost = f.Name.Length + (f.Gloss?.Length ?? 0) + 16;
                if (used + cost > budget && result.Count > 0) continue; // drop tail overflow rather than block all
                result.Add(f);
                used += cost;
            }
            return result;
        }

        private static string Format(List<SceneFact> chosen)
        {
            if (chosen.Count == 0) return "（当前没有值得特别注意的周边事物。）";
            var sb = new StringBuilder();
            sb.Append("【当前场景】");
            foreach (SceneFact f in chosen)
            {
                sb.Append('\n').Append("· ").Append(f.Name);
                if (!string.IsNullOrEmpty(f.Gloss)) sb.Append("（").Append(f.Gloss).Append("）");
            }
            return sb.ToString();
        }

        // ---------------- sources ----------------

        private class ActorsSource : ISceneSource
        {
            public string SourceName { get { return "Actors"; } }

            public void Collect(SceneQuery q, List<SceneFact> into)
            {
                var comp = RimTavernGameComp.Get();
                List<Pawn> all = q.Map.mapPawns.AllPawns;
                if (all == null) return;
                float centerDist = 14f;
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p == null || p.Dead || !p.Spawned) continue;
                    bool focus = p == q.Focus;
                    bool other = p == q.Other;
                    bool cast = comp != null && comp.IsCastOrProtagonist(p);
                    bool nearby = !focus && !other && p.Position.DistanceTo(q.Focus.Position) <= centerDist;
                    if (!focus && !other && !cast && !nearby) continue;

                    var f = new SceneFact();
                    f.Category = SceneFactCategory.Actor;
                    f.Name = p.LabelShort;
                    f.Gloss = p.def != null ? p.def.label : "";
                    f.Salience = focus || other ? 1f : (cast ? 0.9f : 0.35f);
                    f.Anchor = "同地图";
                    into.Add(f);
                }
            }
        }

        private class MapContentsSource : ISceneSource
        {
            public string SourceName { get { return "MapContents"; } }

            public void Collect(SceneQuery q, List<SceneFact> into)
            {
                if (q.Focus == null || !q.Focus.Spawned) return;
                Map map = q.Map;
                int radius = 5;
                var items = new Dictionary<string, int>(StringComparer.Ordinal);
                var itemGloss = new Dictionary<string, string>(StringComparer.Ordinal);
                var buildings = new List<SceneFact>();
                var seenB = new HashSet<string>();

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        IntVec3 cell = q.Focus.Position + new IntVec3(dx, 0, dz);
                        if (!cell.InBounds(map)) continue;
                        if (cell.DistanceTo(q.Focus.Position) > radius) continue;
                        List<Thing> things = map.thingGrid.ThingsListAt(cell);
                        for (int t = 0; t < things.Count; t++)
                        {
                            Thing th = things[t];
                            if (th == null || th.def == null) continue;
                            if (th.def.category == ThingCategory.Pawn) continue;
                            if (th.def.category == ThingCategory.Plant) continue;
                            if (th.def.category == ThingCategory.Building)
                            {
                                // structural (walls etc.) are non-standable; keep usable/notable buildings generically
                                if (th.def.passability != Traversability.Standable) continue;
                                string name = th.def.label;
                                if (seenB.Add(name))
                                {
                                    buildings.Add(new SceneFact
                                    {
                                        Category = SceneFactCategory.MapThing,
                                        Name = name,
                                        Gloss = GlossForShort(th.def, 70),
                                        Salience = 0.35f,
                                        Anchor = "附近"
                                    });
                                }
                                continue;
                            }
                            if (th.def.category == ThingCategory.Item)
                            {
                                string name = th.def.label;
                                items.TryGetValue(name, out int c);
                                items[name] = c + 1;
                                if (!itemGloss.ContainsKey(name)) itemGloss[name] = GlossForShort(th.def, 70);
                            }
                        }
                    }
                }

                foreach (var kv in items)
                {
                    string count = kv.Value > 1 ? " ×" + kv.Value : "";
                    into.Add(new SceneFact
                    {
                        Category = SceneFactCategory.MapThing,
                        Name = kv.Key + count,
                        Gloss = itemGloss[kv.Key],
                        Salience = 0.18f,
                        Anchor = "附近"
                    });
                }
                into.AddRange(buildings);
            }
        }

        private class EquipmentSource : ISceneSource
        {
            public string SourceName { get { return "Equipment"; } }

            public void Collect(SceneQuery q, List<SceneFact> into)
            {
                AddFor(q.Focus, into, 0.55f);
                if (q.Other != null && q.Other != q.Focus) AddFor(q.Other, into, 0.4f);
            }

            private static void AddFor(Pawn p, List<SceneFact> into, float baseSal)
            {
                if (p == null || p.equipment == null && p.apparel == null) return;
                var equipment = p.equipment?.Primary;
                if (equipment?.def != null)
                {
                    into.Add(new SceneFact
                    {
                        Category = SceneFactCategory.Equipment,
                        Name = equipment.def.label + "（装备）",
                        Gloss = GlossFor(equipment.def),
                        Salience = baseSal,
                        Anchor = "随身"
                    });
                }
                if (p.apparel != null && p.apparel.WornApparel != null)
                {
                    int count = 0;
                    foreach (Apparel a in p.apparel.WornApparel)
                    {
                        if (a?.def == null || ++count > 1) break;
                        into.Add(new SceneFact
                        {
                            Category = SceneFactCategory.Equipment,
                            Name = a.def.label + "（穿着）",
                            Gloss = GlossFor(a.def),
                            Salience = baseSal - 0.15f,
                            Anchor = "随身"
                        });
                    }
                }
            }
        }

        private class ContainerSource : ISceneSource
        {
            public string SourceName { get { return "Container"; } }

            public void Collect(SceneQuery q, List<SceneFact> into)
            {
                if (q.Focus == null || !q.Focus.Spawned) return;
                Room room = RegionAndRoomQuery.RoomAt(q.Focus.Position, q.Map, RegionType.Set_Passable);
                string where;
                if (room != null)
                {
                    bool hasRole = room.Role != null && room.Role.defName != "None"
                                   && !string.IsNullOrEmpty(room.Role.label);
                    where = hasRole ? room.Role.label + "内" : "一个房间里";
                }
                else
                {
                    where = "开阔地带（" + (q.Map.Biome != null ? q.Map.Biome.label : "") + "）";
                }
                into.Add(new SceneFact
                {
                    Category = SceneFactCategory.Container,
                    Name = "两人位于" + where,
                    Salience = 1f,
                    Anchor = "位置"
                });
            }
        }

        private class WorldSource : ISceneSource
        {
            public string SourceName { get { return "World"; } }

            public void Collect(SceneQuery q, List<SceneFact> into)
            {
                // Minimal for P2: the speakers' faction context. Quests/world objects/Delta arrive later.
                foreach (Pawn p in new[] { q.Focus, q.Other })
                {
                    if (p == null || p.Faction == null || p.Faction.def == null) continue;
                    into.Add(new SceneFact
                    {
                        Category = SceneFactCategory.World,
                        Name = p.LabelShort + " 属于" + p.Faction.def.label,
                        Gloss = GlossFor(p.Faction.def),
                        Salience = 0.5f,
                        Anchor = "派系"
                    });
                }
            }
        }
    }
}
