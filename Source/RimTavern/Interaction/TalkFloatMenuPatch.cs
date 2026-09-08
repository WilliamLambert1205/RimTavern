using System.Collections.Generic;
using HarmonyLib;
using RimTavern.Core;
using RimTavern.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTavern.Interaction
{
    /// <summary>
    /// Right-click entry points (RimWorld 1.6 FloatMenuMakerMap.GetOptions):
    /// 1. Right-click the selected colonist itself  -> "设为故事主角"
    /// 2. Right-click another pawn while the protagonist is selected -> "与 X 交谈"
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class TalkFloatMenuPatch
    {
        private const int ClickRadiusCells = 1;

        public static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            if (__result == null || selectedPawns == null || selectedPawns.Count != 1) return;

            Pawn selected = selectedPawns[0];
            if (selected == null || selected.Dead || !selected.Spawned || !selected.RaceProps.Humanlike) return;

            Map map = selected.Map;
            if (map == null) return;

            var comp = RimTavernGameComp.Get();
            if (comp == null) return;

            IntVec3 clickCell = IntVec3.FromVector3(clickPos);
            var seen = new HashSet<Pawn>();

            for (int dx = -ClickRadiusCells; dx <= ClickRadiusCells; dx++)
            {
                for (int dz = -ClickRadiusCells; dz <= ClickRadiusCells; dz++)
                {
                    IntVec3 cell = clickCell + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(map)) continue;

                    List<Thing> things = map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Pawn hit && seen.Add(hit))
                        {
                            TryAddOptions(comp, selected, hit, __result);
                        }
                    }
                }
            }
        }

        private static void TryAddOptions(RimTavernGameComp comp, Pawn selected, Pawn hit, List<FloatMenuOption> result)
        {
            // 1) Right-click the selected colonist itself: (re)designate protagonist.
            if (hit == selected)
            {
                bool isCurrent = comp.HasProtagonist && comp.TryGetProtagonist(out Pawn cur, false) && cur == selected;
                if (!isCurrent && CanBeProtagonist(selected))
                {
                    Pawn sel = selected;
                    result.Add(new FloatMenuOption(
                        "设为故事主角：" + sel.LabelShort,
                        delegate
                        {
                            comp.SetProtagonist(sel);
                            Messages.Message(sel.LabelShort + " 现在是故事主角。", new LookTargets(sel), MessageTypeDefOf.NeutralEvent, false);
                        },
                        MenuOptionPriority.Default, null, sel));
                }

                // 1b) Toggle cast membership for any free colonist (P1-4).
                if (CanBeProtagonist(selected))
                {
                    Pawn sel = selected;
                    bool inCast = comp.IsCast(sel);
                    string label = inCast ? "移出常驻配角：" : "设为常驻配角：";
                    result.Add(new FloatMenuOption(
                        label + sel.LabelShort,
                        delegate
                        {
                            comp.ToggleCast(sel);
                            Messages.Message(sel.LabelShort + (comp.IsCast(sel) ? " 现在是常驻配角。" : " 已移出常驻配角。"),
                                new LookTargets(sel), MessageTypeDefOf.NeutralEvent, false);
                        },
                        MenuOptionPriority.Default, null, sel));
                }
                return;
            }

            // 2) Selected pawn == protagonist: open a dialogue with the right-clicked humanlike pawn.
            if (!CanBeProtagonist(selected)) return;
            if (!comp.TryGetProtagonist(out Pawn proto, true) || proto != selected) return;
            if (hit == proto || !CanTalkTo(hit)) return;

            Pawn actor = proto;
            Pawn npc = hit;
            result.Add(new FloatMenuOption(
                "与 " + npc.LabelShort + " 交谈（RimTavern）",
                delegate { Find.WindowStack.Add(new TavernDialogueWindow(actor, npc)); },
                MenuOptionPriority.Default, null, npc));
        }

        private static bool CanBeProtagonist(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned) return false;
            if (!p.RaceProps.Humanlike) return false;
            return p.IsFreeColonist;
        }

        private static bool CanTalkTo(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned) return false;
            return p.RaceProps.Humanlike;
        }
    }
}
