using System;
using System.Collections.Generic;
using RimTavern.Data;
using Verse;

namespace RimTavern.Core
{
    /// <summary>
    /// Per-savegame story state (protagonist/cast + P4 structured memory & affinity).
    /// GameComponent subclasses are auto-instantiated by RimWorld.
    /// </summary>
    public class RimTavernGameComp : GameComponent
    {
        private int protagonistId = -1;
        private int storyNextEventTick = 0;
        private List<int> castIds = new List<int>();
        private Dictionary<int, string> pawnCardBindings = new Dictionary<int, string>();

        // P4: structured memory + protagonist-centric affinity
        private List<MemoryRecord> memories = new List<MemoryRecord>();
        private Dictionary<int, int> affinityByNpc = new Dictionary<int, int>();
        private Dictionary<int, int> lastInteractTick = new Dictionary<int, int>();
        private const int MemCap = 120;
        private List<SavedDialogue> dialogueArchive = new List<SavedDialogue>();

        // P5: story events
        private Dictionary<string, bool> storyFlags = new Dictionary<string, bool>();
        private HashSet<string> unlockedEvents = new HashSet<string>();
        private Dictionary<string, int> eventTimes = new Dictionary<string, int>();
        private Dictionary<string, int> eventLastFired = new Dictionary<string, int>();
        private List<EventInstance> activeEvents = new List<EventInstance>();
        private int lastDialogueNpcId = -1;
        private int lastDialogueTick = -1;
        private int eventScanCounter = 0;

        // outcomes produced on background threads at dialogue end; drained on the main thread
        private static readonly object PendingLock = new object();
        private static readonly List<MemoryOutcome> PendingOutcomes = new List<MemoryOutcome>();

        private const int RetryTicks = 2500; // 1 in-game hour

        // RimWorld instantiates subclasses via Activator.CreateInstance(type, new object[] { game }),
        // so the constructor must accept Game but must not forward to a non-existent base ctor.
        public RimTavernGameComp(Game game)
        {
        }

        public static RimTavernGameComp Get()
        {
            if (Verse.Current.Game == null) return null;
            return Verse.Current.Game.GetComponent<RimTavernGameComp>();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref protagonistId, "protagonistId", -1, false);
            Scribe_Values.Look(ref storyNextEventTick, "storyNextEventTick", 0, false);
            Scribe_Collections.Look(ref castIds, "castIds", LookMode.Value);
            Scribe_Collections.Look(ref pawnCardBindings, "pawnCardBindings", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref memories, "memories", LookMode.Deep);
            Scribe_Collections.Look(ref affinityByNpc, "affinityByNpc", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastInteractTick, "lastInteractTick", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref dialogueArchive, "dialogueArchive", LookMode.Deep);
            Scribe_Collections.Look(ref storyFlags, "storyFlags", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref unlockedEvents, "unlockedEvents", LookMode.Value);
            Scribe_Collections.Look(ref eventTimes, "eventTimes", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref eventLastFired, "eventLastFired", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref activeEvents, "activeEvents", LookMode.Deep);
            Scribe_Values.Look(ref lastDialogueNpcId, "lastDialogueNpcId", -1, false);
            Scribe_Values.Look(ref lastDialogueTick, "lastDialogueTick", -1, false);
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ScheduleNextEvent();
            RefreshUnlockedEvents();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            if (storyNextEventTick <= 0)
            {
                ScheduleNextEvent();
            }
            RefreshUnlockedEvents();
        }

        private void ScheduleNextEvent()
        {
            if (Find.TickManager == null) return;
            storyNextEventTick = Find.TickManager.TicksGame + IntervalTicks();
        }

        private int IntervalTicks()
        {
            int hours = RimTavernMod.Settings != null ? RimTavernMod.Settings.storyEventIntervalHours : 12;
            if (hours < 1) hours = 1;
            return hours * 2500;
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            DrainPendingOutcomes();
            if ((++eventScanCounter % 600) == 0)
            {
                if (RimTavernMod.Settings != null && RimTavernMod.Settings.storyEventsEnabled)
                {
                    TryFireNextEvent(false, out _);
                }
            }
            try
            {
                if (RimTavernMod.Settings == null || !RimTavernMod.Settings.storyEventsEnabled) return;
                if (Find.TickManager == null) return;
                int now = Find.TickManager.TicksGame;
                if (now < storyNextEventTick) return;

                if (TryOpenAutoStoryDialogue(false))
                {
                    storyNextEventTick = now + IntervalTicks();
                }
                else
                {
                    storyNextEventTick = now + RetryTicks; // conditions not met; retry next in-game hour
                }
            }
            catch (Exception ex)
            {
                Log.Error("[RimTavern] story scheduler tick error: " + ex);
                storyNextEventTick = (Find.TickManager != null ? Find.TickManager.TicksGame : 0) + IntervalTicks();
            }
        }

        /// <summary>
        /// Auto-event: a random suitable humanlike pawn on the protagonist's map walks up and
        /// starts a dialogue (npcInitiated window). Returns false when not possible right now.
        /// </summary>
        public bool TryOpenAutoStoryDialogue(bool allowWithOtherWindows = false)
        {
            if (RimTavern.UI.TavernDialogueWindow.OpenCount > 0) return false;
            if (!allowWithOtherWindows && (Find.WindowStack == null || Find.WindowStack.Count > 0)) return false;
            if (!TryGetProtagonist(out Pawn proto, true)) return false;

            Map map = proto.Map;
            if (map == null) return false;
            if (proto.Drafted || proto.Downed || proto.InMentalState) return false;

            var cand = new List<Pawn>();
            List<Pawn> all = map.mapPawns.AllPawns;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p != null && p != proto && p.RaceProps.Humanlike && p.Spawned && !p.Dead
                        && !p.Downed && !p.Drafted && p.Map == map)
                    {
                        cand.Add(p);
                    }
                }
            }
            if (cand.Count == 0) return false;

            Pawn npc = cand.RandomElement();
            Find.WindowStack.Add(new RimTavern.UI.TavernDialogueWindow(proto, npc, true));
            return true;
        }

        public bool HasProtagonist
        {
            get { return protagonistId > 0; }
        }

        /// <summary>Resolve the stored protagonist. requireUsable=false allows dead/absent check skip only for display.</summary>
        public bool TryGetProtagonist(out Pawn pawn, bool requireUsable = true)
        {
            pawn = null;
            if (protagonistId <= 0) return false;
            if (Find.Maps == null) return false;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                List<Pawn> all = Find.Maps[i].mapPawns.AllPawns;
                if (all == null) continue;
                for (int j = 0; j < all.Count; j++)
                {
                    Pawn p = all[j];
                    if (p != null && p.thingIDNumber == protagonistId)
                    {
                        if (requireUsable && (p.Dead || !p.Spawned)) return false;
                        pawn = p;
                        return true;
                    }
                }
            }
            return false;
        }

        public void SetProtagonist(Pawn p)
        {
            if (p == null) return;
            protagonistId = p.thingIDNumber;
        }

        public void ClearProtagonist()
        {
            protagonistId = -1;
        }

        // ---- cast（常驻配角名单，P1-4）----

        public bool IsCast(Pawn p)
        {
            return p != null && castIds.Contains(p.thingIDNumber);
        }

        public void ToggleCast(Pawn p)
        {
            if (p == null) return;
            if (castIds.Contains(p.thingIDNumber)) castIds.Remove(p.thingIDNumber);
            else castIds.Add(p.thingIDNumber);
        }

        public bool IsCastOrProtagonist(Pawn p)
        {
            if (p == null) return false;
            if (IsCast(p)) return true;
            return p.thingIDNumber == protagonistId;
        }

        // ---- character-card bindings (per-save, adjustable; P1 assignment) ----

        public string GetBoundCardUid(int pawnId)
        {
            if (pawnId <= 0) return null;
            return pawnCardBindings.TryGetValue(pawnId, out string uid) ? uid : null;
        }

        public void SetCardBinding(int pawnId, string cardUid)
        {
            if (pawnId <= 0) return;
            if (string.IsNullOrEmpty(cardUid)) pawnCardBindings.Remove(pawnId);
            else pawnCardBindings[pawnId] = cardUid;
        }

        public void ClearCardBinding(int pawnId)
        {
            if (pawnId > 0) pawnCardBindings.Remove(pawnId);
        }

        // ---------------- P4: structured memory & affinity ----------------

        public int GetAffinity(int npcId)
        {
            return affinityByNpc.TryGetValue(npcId, out int v) ? v : 0;
        }

        /// <summary>Queue a background-produced outcome; drained on the main thread each tick.</summary>
        public static void EnqueueOutcome(MemoryOutcome outcome)
        {
            if (outcome == null) return;
            lock (PendingLock)
            {
                PendingOutcomes.Add(outcome);
                if (PendingOutcomes.Count > 16) PendingOutcomes.RemoveAt(0);
            }
        }

        private void DrainPendingOutcomes()
        {
            List<MemoryOutcome> batch = null;
            lock (PendingLock)
            {
                if (PendingOutcomes.Count > 0)
                {
                    batch = new List<MemoryOutcome>(PendingOutcomes);
                    PendingOutcomes.Clear();
                }
            }
            if (batch == null) return;
            foreach (MemoryOutcome o in batch)
            {
                ApplyOutcome(o);
            }
        }

        private void ApplyOutcome(MemoryOutcome o)
        {
            if (o == null || o.npcId <= 0) return;
            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            affinityByNpc.TryGetValue(o.npcId, out int cur);
            int next = cur + o.affinityDelta;
            if (next < -100) next = -100;
            if (next > 100) next = 100;
            affinityByNpc[o.npcId] = next;
            lastInteractTick[o.npcId] = tick;
            if (!string.IsNullOrEmpty(o.summary))
            {
                AddMemory((int)MemKind.Chat, o.npcId, 3, o.summary);
                RimTavern.Util.Diag.Log("mem|outcome",
                    "好感 " + cur + "→" + next + "；记忆：" + (o.summary.Length > 120 ? o.summary.Substring(0, 120) + "…" : o.summary));
            }
        }

        public void AddMemory(int kind, int npcId, int importance, string summary)
        {
            if (string.IsNullOrEmpty(summary)) return;
            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            memories.Add(new MemoryRecord
            {
                kind = kind,
                tick = tick,
                npcId = npcId,
                importance = importance,
                summary = summary.Trim()
            });
            if (memories.Count > MemCap)
            {
                memories.Sort((a, b) => a.tick.CompareTo(b.tick));
                memories.RemoveRange(0, memories.Count - MemCap);
            }
        }

        /// <summary>Recall text for one NPC (main thread). Empty string when nothing relevant.</summary>
        public string MemoryPromptText(int npcId, int maxLines, int maxChars)
        {
            if (memories.Count == 0 || npcId <= 0 || maxLines <= 0) return "";
            var list = new List<MemoryRecord>();
            foreach (MemoryRecord m in memories)
            {
                if (m.npcId == npcId) list.Add(m);
            }
            if (list.Count == 0) return "";
            list.Sort((a, b) => b.tick.CompareTo(a.tick));
            var sb = new System.Text.StringBuilder();
            sb.Append("【此前记忆】");
            int used = sb.Length;
            int added = 0;
            foreach (MemoryRecord m in list)
            {
                if (added >= maxLines) break;
                string line = "·" + MemoryRecord.KindLabel(m.kind) + "：" + m.summary;
                if (used + line.Length > maxChars && added > 0) break;
                sb.Append('\n').Append(line);
                used += line.Length;
                added++;
            }
            return added > 0 ? sb.ToString() : "";
        }

        // ---------------- dialogue archive (history persistence, overlay seed) ----------------

        public int ArchiveCount { get { return dialogueArchive.Count; } }

        public void ArchiveDialogue(int tick, string protoLabel, string npcLabel, List<SavedLine> lines)
        {
            if (lines == null || lines.Count == 0) return;
            dialogueArchive.Add(new SavedDialogue(tick, protoLabel, npcLabel, lines));
            while (dialogueArchive.Count > 60) dialogueArchive.RemoveAt(0); // keep recent only
        }

        /// <summary>Newest-first snapshot for the history window.</summary>
        public List<SavedDialogue> GetArchiveSnapshot()
        {
            var snap = new List<SavedDialogue>(dialogueArchive);
            snap.Reverse(); // newest first
            return snap;
        }

        /// <summary>Lines of the most recent archived dialogue (for seeding the overlay after load).</summary>
        public List<SavedLine> GetLastDialogueLines(int max)
        {
            if (dialogueArchive.Count == 0) return new List<SavedLine>();
            SavedDialogue last = dialogueArchive[dialogueArchive.Count - 1];
            int start = Math.Max(0, last.lines.Count - max);
            return last.lines.GetRange(start, last.lines.Count - start);
        }

        // ---------------- P5: story event engine (v1 core) ----------------

        public void RefreshUnlockedEvents()
        {
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (StoryEventDef d in EventStore.All)
            {
                if (d.nextIds != null)
                {
                    foreach (string n in d.nextIds) referenced.Add(n);
                }
            }
            unlockedEvents.Clear();
            foreach (StoryEventDef d in EventStore.All)
            {
                if (d.initial || !referenced.Contains(d.id)) unlockedEvents.Add(d.id);
            }
        }

        public bool GetStoryFlag(string flag)
        {
            return !string.IsNullOrEmpty(flag) && storyFlags.TryGetValue(flag, out bool v) && v;
        }

        public void SetStoryFlag(string flag, bool value)
        {
            if (string.IsNullOrEmpty(flag)) return;
            if (value) storyFlags[flag] = true;
            else storyFlags.Remove(flag);
        }

        /// <summary>Call when any dialogue closes: remembers last speaker and completes matching event instances.</summary>
        public void RecordDialogueEnded(int npcId, int tick)
        {
            if (npcId > 0)
            {
                lastDialogueNpcId = npcId;
                lastDialogueTick = tick;
            }
            if (npcId <= 0) return;
            for (int i = activeEvents.Count - 1; i >= 0; i--)
            {
                EventInstance inst = activeEvents[i];
                if (inst.done || inst.targetNpcId != npcId) continue;
                inst.done = true;
                activeEvents.RemoveAt(i);
                CompleteEvent(inst);
            }
        }

        private void CompleteEvent(EventInstance inst)
        {
            StoryEventDef d = EventStore.Get(inst.defId);
            if (d == null) return;
            eventTimes.TryGetValue(d.id, out int t);
            eventTimes[d.id] = t + 1;
            foreach (string f in d.setFlagsOnDone) SetStoryFlag(f, true);
            foreach (string nid in d.nextIds)
            {
                if (EventStore.Get(nid) != null) unlockedEvents.Add(nid);
            }
            RimTavern.Util.Diag.Log("ev|done", "事件完成：" + d.id + "（" + d.title + "）");
        }

        public bool TryFireNextEvent(bool force, out string msg)
        {
            msg = "";
            if (EventStore.Count == 0)
            {
                msg = "无事件定义（Events/ 为空）。";
                return false;
            }
            if (RimTavern.UI.TavernDialogueWindow.OpenCount > 0)
            {
                msg = "已有对话进行中。";
                return false;
            }
            if (!TryGetProtagonist(out Pawn proto, true))
            {
                msg = "主角不可用。";
                return false;
            }
            Map map = proto.Map;
            if (map == null)
            {
                msg = "主角不在地图。";
                return false;
            }
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            // skip defs with an unfinished instance
            var busy = new HashSet<string>();
            foreach (EventInstance a in activeEvents) if (!a.done) busy.Add(a.defId);

            var reasons = new List<string>();
            var chosen = new List<StoryEventDef>();
            foreach (StoryEventDef d in EventStore.All)
            {
                if (!unlockedEvents.Contains(d.id)) { reasons.Add(d.id + "=未解锁"); continue; }
                if (busy.Contains(d.id)) { reasons.Add(d.id + "=进行中"); continue; }
                eventTimes.TryGetValue(d.id, out int done);
                if (done >= d.maxTimes) { reasons.Add(d.id + "=次数用尽"); continue; }
                chosen.Add(d);
            }

            foreach (StoryEventDef d in chosen)
            {
                if (force)
                {
                    // debug: bypass cooldown/timing, just open the event with a usable NPC
                    Pawn fn = PickNpcByAffinity(proto, map, d);
                    if (fn == null) { reasons.Add(d.id + "=无候选NPC"); continue; }
                    FireEvent(d, fn, now);
                    msg = "触发事件：" + d.id + "（" + d.title + " → " + fn.LabelShort + "）";
                    return true;
                }
                if (!CooldownPassed(d, now)) { reasons.Add(d.id + "=冷却中"); continue; }
                Pawn npc = null;
                bool due = false;
                switch (d.trigger)
                {
                    case EvTriggerKind.Interval:
                        due = !eventLastFired.ContainsKey(d.id) || CooldownPassedTicks(d.id, now, Rand.RangeInclusive(d.intervalMinHours, d.intervalMaxHours) * 2500, false);
                        npc = PickNpc(proto, map, d);
                        break;
                    case EvTriggerKind.FlagSet:
                        due = GetStoryFlag(d.afterFlag);
                        npc = PickNpc(proto, map, d);
                        break;
                    case EvTriggerKind.AfterDialogue:
                        if (lastDialogueNpcId > 0 && lastDialogueTick > 0
                            && (now - lastDialogueTick) <= d.afterDialogueWindowHours * 2500)
                        {
                            Pawn pn = FindPawnById(lastDialogueNpcId);
                            if (pn != null && MatchesNpc(pn, proto, d.npcSelector))
                            {
                                due = true;
                                npc = pn;
                            }
                        }
                        break;
                    case EvTriggerKind.Affinity:
                        npc = PickNpcByAffinity(proto, map, d);
                        due = npc != null;
                        break;
                }
                if (!due) { reasons.Add(d.id + "=条件未达(" + d.DescribeTrigger() + ")"); continue; }
                if (npc == null) { reasons.Add(d.id + "=无候选NPC"); continue; }

                FireEvent(d, npc, now);
                msg = "触发事件：" + d.id + "（" + d.title + " → " + npc.LabelShort + "）";
                return true;
            }

            msg = "解锁" + unlockedEvents.Count + "/总" + EventStore.Count + "/候选" + chosen.Count
                  + "；原因：" + (reasons.Count > 0 ? string.Join("；", reasons) : "无");
            if (force)
            {
                RimTavern.Util.Diag.Log("ev|scan", msg);
            }
            return false;
        }

        private void FireEvent(StoryEventDef d, Pawn npc, int now)
        {
            if (!TryGetProtagonist(out Pawn proto, true)) return;
            eventLastFired[d.id] = now;
            activeEvents.Add(new EventInstance { defId = d.id, targetNpcId = npc.thingIDNumber, tickOpened = now });
            Find.WindowStack.Add(new RimTavern.UI.TavernDialogueWindow(proto, npc, true, d.opening, d.loreKeys));
            RimTavern.Util.Diag.Log("ev|fire", d.id + " → " + npc.LabelShort + "（" + d.DescribeTrigger() + "）");
        }

        private bool CooldownPassed(StoryEventDef d, int now)
        {
            return CooldownPassedTicks(d.id, now, d.cooldownHours * 2500, true);
        }

        private bool CooldownPassedTicks(string id, int now, int needTicks, bool requireRecord)
        {
            if (!eventLastFired.TryGetValue(id, out int last)) return !requireRecord;
            return (now - last) >= needTicks;
        }

        private Pawn FindPawnById(int id)
        {
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

        private Pawn PickNpc(Pawn proto, Map map, StoryEventDef d)
        {
            return PickNpcByAffinity(proto, map, d);
        }

        private Pawn PickNpcByAffinity(Pawn proto, Map map, StoryEventDef d)
        {
            List<Pawn> all = map.mapPawns.AllPawns;
            if (all == null) return null;
            var basic = new List<Pawn>();
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p == proto || p.Dead || !p.Spawned || !p.RaceProps.Humanlike) continue;
                basic.Add(p);
            }
            var filtered = new List<Pawn>();
            foreach (Pawn p in basic)
            {
                if (!MatchesNpc(p, proto, d.npcSelector)) continue;
                if (InAffinityRange(p, d)) filtered.Add(p);
            }
            // fallback: selector too strict (e.g. no cast yet) -> any usable pawn
            if (filtered.Count == 0)
            {
                foreach (Pawn p in basic)
                {
                    if (InAffinityRange(p, d)) filtered.Add(p);
                }
            }
            return filtered.Count == 0 ? null : filtered.RandomElement();
        }

        private bool InAffinityRange(Pawn p, StoryEventDef d)
        {
            if (d.affinityMin <= -101 && d.affinityMax >= 101) return true;
            int aff = GetAffinity(p.thingIDNumber);
            return aff >= d.affinityMin && aff <= d.affinityMax;
        }

        private bool MatchesNpc(Pawn p, Pawn proto, string selector)
        {
            switch ((selector ?? "cast").ToLowerInvariant())
            {
                case "cast":
                    return IsCast(p);
                case "colonist":
                    return p.IsFreeColonist;
                case "visitor":
                    if (p.IsFreeColonist) return false;
                    return p.Faction != null && !p.Faction.IsPlayer;
                default:
                    return true; // any
            }
        }
    }
}
