using System;
using System.Collections.Generic;
using System.IO;
using RimTavern.Util;
using Verse;

namespace RimTavern.Data
{
    /// <summary>Trigger kinds supported by the P5 event engine (v1).</summary>
    public enum EvTriggerKind { Interval, AfterDialogue, FlagSet, Affinity }

    /// <summary>P5: author-defined story event (Events/*.json).</summary>
    public class StoryEventDef
    {
        public string id = "";
        public string title = "";
        public string opening = "";      // injected as the npcInitiated scene note
        public string npcSelector = "cast"; // cast | colonist | visitor | any
        public EvTriggerKind trigger = EvTriggerKind.Interval;
        public int intervalMinHours = 8;
        public int intervalMaxHours = 24;
        public int afterDialogueWindowHours = 24;
        public string afterFlag = "";     // FlagSet: fires when this story flag is true
        public int affinityMin = -101;    // bounds disabled by default
        public int affinityMax = 101;
        public int cooldownHours = 24;
        public bool initial = true;       // unlocked at start (unless a chain parent lists it)
        public int maxTimes = 99;
        public string sourceFile = ""; // set at load; used to tell imported vs in-game user entries
        public List<string> nextIds = new List<string>();
        public List<string> setFlagsOnDone = new List<string>();
        public List<string> loreKeys = new List<string>(); // reserved: force-activate worldbook entries on opening

        public StoryEventDef Copy()
        {
            var c = new StoryEventDef();
            c.id = id; c.title = title; c.opening = opening; c.npcSelector = npcSelector;
            c.trigger = trigger; c.intervalMinHours = intervalMinHours; c.intervalMaxHours = intervalMaxHours;
            c.afterDialogueWindowHours = afterDialogueWindowHours; c.afterFlag = afterFlag;
            c.affinityMin = affinityMin; c.affinityMax = affinityMax; c.cooldownHours = cooldownHours;
            c.initial = initial; c.maxTimes = maxTimes; c.sourceFile = sourceFile;
            c.nextIds = new List<string>(nextIds);
            c.setFlagsOnDone = new List<string>(setFlagsOnDone);
            c.loreKeys = new List<string>(loreKeys);
            return c;
        }

        public RimTavern.Util.JsonObject ToJsonNode()
        {
            var o = new RimTavern.Util.JsonObject();
            o.Fields["id"] = new RimTavern.Util.JsonString(id);
            o.Fields["title"] = new RimTavern.Util.JsonString(title);
            o.Fields["opening"] = new RimTavern.Util.JsonString(opening);
            o.Fields["npc"] = new RimTavern.Util.JsonString(npcSelector);
            o.Fields["trigger"] = new RimTavern.Util.JsonString(
                trigger == EvTriggerKind.AfterDialogue ? "afterDialogue"
                : trigger == EvTriggerKind.FlagSet ? "flagSet"
                : trigger == EvTriggerKind.Affinity ? "affinity" : "interval");
            o.Fields["intervalMinHours"] = new RimTavern.Util.JsonNumber(intervalMinHours);
            o.Fields["intervalMaxHours"] = new RimTavern.Util.JsonNumber(intervalMaxHours);
            o.Fields["afterDialogueWindowHours"] = new RimTavern.Util.JsonNumber(afterDialogueWindowHours);
            o.Fields["afterFlag"] = new RimTavern.Util.JsonString(afterFlag);
            o.Fields["affinityMin"] = new RimTavern.Util.JsonNumber(affinityMin);
            o.Fields["affinityMax"] = new RimTavern.Util.JsonNumber(affinityMax);
            o.Fields["cooldownHours"] = new RimTavern.Util.JsonNumber(cooldownHours);
            o.Fields["initial"] = new RimTavern.Util.JsonBool(initial);
            o.Fields["maxTimes"] = new RimTavern.Util.JsonNumber(maxTimes);
            o.Fields["nextIds"] = StrNode(nextIds);
            o.Fields["setFlagsOnDone"] = StrNode(setFlagsOnDone);
            o.Fields["loreKeys"] = StrNode(loreKeys);
            return o;
        }

        private static RimTavern.Util.JsonNode StrNode(List<string> list)
        {
            var a = new RimTavern.Util.JsonArray();
            foreach (string s in list) a.Items.Add(new RimTavern.Util.JsonString(s ?? ""));
            return a;
        }

        public string DescribeTrigger()
        {
            switch (trigger)
            {
                case EvTriggerKind.Interval:
                    return "interval " + intervalMinHours + "~" + intervalMaxHours + "h";
                case EvTriggerKind.AfterDialogue:
                    return "afterDialogue(" + afterDialogueWindowHours + "h)";
                case EvTriggerKind.FlagSet:
                    return "flag:" + afterFlag;
                case EvTriggerKind.Affinity:
                    return "affinity[" + affinityMin + "," + affinityMax + "]";
                default: return trigger.ToString();
            }
        }

        public static StoryEventDef Parse(JsonNode n)
        {
            var d = new StoryEventDef();
            d.id = n.GetStr("id", "");
            d.title = n.GetStr("title", "");
            d.opening = n.GetStr("opening", "");
            d.npcSelector = n.GetStr("npc", "cast");
            string tr = n.GetStr("trigger", "interval");
            d.trigger = tr == "afterDialogue" ? EvTriggerKind.AfterDialogue
                : tr == "flagSet" ? EvTriggerKind.FlagSet
                : tr == "affinity" ? EvTriggerKind.Affinity : EvTriggerKind.Interval;
            d.intervalMinHours = (int)n.GetNum("intervalMinHours", 8);
            d.intervalMaxHours = (int)n.GetNum("intervalMaxHours", 24);
            d.afterDialogueWindowHours = (int)n.GetNum("afterDialogueWindowHours", 24);
            d.afterFlag = n.GetStr("afterFlag", "");
            d.affinityMin = (int)n.GetNum("affinityMin", -101);
            d.affinityMax = (int)n.GetNum("affinityMax", 101);
            d.cooldownHours = (int)n.GetNum("cooldownHours", 24);
            d.initial = n.GetBool("initial", true);
            d.maxTimes = (int)n.GetNum("maxTimes", 99);
            d.nextIds = StrArr(n.Get("nextIds"));
            d.setFlagsOnDone = StrArr(n.Get("setFlagsOnDone"));
            d.loreKeys = StrArr(n.Get("loreKeys"));
            return d;
        }

        private static List<string> StrArr(JsonNode arr)
        {
            var list = new List<string>();
            if (arr is JsonArray a)
            {
                foreach (JsonNode x in a.Items)
                {
                    string s = JsonNode.Str(x);
                    if (!string.IsNullOrEmpty(s)) list.Add(s.Trim());
                }
            }
            return list;
        }
    }

    /// <summary>P5: one in-flight (or pending) event instance.</summary>
    public class EventInstance : IExposable
    {
        public string defId = "";
        public int targetNpcId = -1;
        public int tickOpened;
        public bool done;

        public void ExposeData()
        {
            Scribe_Values.Look(ref defId, "defId", "", false);
            Scribe_Values.Look(ref targetNpcId, "targetNpcId", -1, false);
            Scribe_Values.Look(ref tickOpened, "tickOpened", 0, false);
            Scribe_Values.Look(ref done, "done", false, false);
        }
    }

    /// <summary>Loads RimTavern/Events/*.json story events; in-game edits persist to the _user events file.</summary>
    public static class EventStore
    {
        public static string EventsDir = "";
        private static readonly Dictionary<string, StoryEventDef> defs = new Dictionary<string, StoryEventDef>(StringComparer.Ordinal);

        public static int Count { get { return defs.Count; } }
        public static IEnumerable<StoryEventDef> All { get { return defs.Values; } }

        public static string UserFilePath()
        {
            return Path.Combine(EventsDir, "_user_events.json");
        }

        public static bool IsUserDef(StoryEventDef d)
        {
            return d != null && d.sourceFile == UserFilePath();
        }

        public static void Init(string eventsDir)
        {
            EventsDir = eventsDir ?? "";
            LoadAll();
        }

        public static void LoadAll()
        {
            defs.Clear();
            if (string.IsNullOrEmpty(EventsDir) || !Directory.Exists(EventsDir)) return;
            foreach (string file in Directory.GetFiles(EventsDir, "*.json"))
            {
                try
                {
                    JsonNode root = Json.Parse(File.ReadAllText(file));
                    if (root == null) continue;
                    if (root.Get("events") is JsonArray arr)
                    {
                        foreach (JsonNode n in arr.Items) AddParsed(n, file);
                    }
                    else
                    {
                        AddParsed(root, file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[RimTavern] 事件解析失败 " + file + ": " + ex.Message);
                }
            }
            Log.Message("[RimTavern] 事件已加载：" + defs.Count + " 条。");
        }

        private static void AddParsed(JsonNode n, string file)
        {
            StoryEventDef d = StoryEventDef.Parse(n);
            if (string.IsNullOrEmpty(d.id)) return;
            d.sourceFile = file;
            if (!defs.ContainsKey(d.id)) defs[d.id] = d;
        }

        public static StoryEventDef Get(string id)
        {
            return !string.IsNullOrEmpty(id) && defs.TryGetValue(id, out StoryEventDef d) ? d : null;
        }

        public static string MakeUniqueId(string baseId)
        {
            string b = string.IsNullOrEmpty(baseId) ? "event" : baseId;
            if (Get(b) == null) return b;
            for (int i = 2; i < 1000; i++)
            {
                string cand = b + "_c" + i;
                if (Get(cand) == null) return cand;
            }
            return b + "_" + DateTime.UtcNow.Ticks.ToString("x");
        }

        /// <summary>Insert or update an in-game authored event (writes the _user events file immediately).</summary>
        public static StoryEventDef UpsertUser(StoryEventDef d)
        {
            if (d == null || string.IsNullOrEmpty(d.id)) return null;
            d.sourceFile = UserFilePath();
            defs[d.id] = d;
            PersistUser();
            return d;
        }

        public static void DeleteUser(string id)
        {
            StoryEventDef d = Get(id);
            if (d == null || !IsUserDef(d)) return;
            defs.Remove(id);
            PersistUser();
        }

        private static void PersistUser()
        {
            try
            {
                if (string.IsNullOrEmpty(EventsDir)) Directory.CreateDirectory(EventsDir);
                else Directory.CreateDirectory(EventsDir);
                var arr = new JsonArray();
                foreach (StoryEventDef d in All)
                {
                    if (IsUserDef(d)) arr.Items.Add(d.ToJsonNode());
                }
                if (arr.Items.Count == 0)
                {
                    if (File.Exists(UserFilePath())) File.Delete(UserFilePath());
                    return;
                }
                var root = new JsonObject();
                root.Fields["events"] = arr;
                File.WriteAllText(UserFilePath(), root.ToJson());
            }
            catch (Exception ex)
            {
                Log.Warning("[RimTavern] 事件用户文件保存失败: " + ex.Message);
            }
        }
    }
}
