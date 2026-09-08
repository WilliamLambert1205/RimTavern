using System;
using System.Collections.Generic;
using RimTavern.Core;
using RimTavern.Data;
using UnityEngine;
using Verse;

namespace RimTavern.UI
{
    /// <summary>P5: in-game editor for story events. Fully scrollable so no field/button is ever clipped.</summary>
    public class EventEditorWindow : Window
    {
        private static readonly string[] TriggerNames = { "interval", "afterDialogue", "flagSet", "affinity" };
        private static readonly string[] NpcNames = { "cast", "colonist", "visitor", "any" };

        private Vector2 listScroll;
        private Vector2 outerScroll;
        private StoryEventDef sel;
        private string idF = "", titleF = "", openingF = "", npcF = "cast", triggerF = "interval";
        private string cooldownF = "24", intMinF = "8", intMaxF = "24", winF = "24", maxTimesF = "99";
        private string affMinF = "-101", affMaxF = "101", flagF = "";
        private string nextF = "", doneFlagsF = "", loreF = "";
        private bool initialF = true;
        private string status = "选择左列事件编辑，或点“新建”。";

        private const float ContentH = 1000f;

        public EventEditorWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(840f, 700f); }
        }

        private static bool IsUser(StoryEventDef d)
        {
            return EventStore.IsUserDef(d);
        }

        public override void DoWindowContents(Rect inRect)
        {
            float contentW = inRect.width - 24f;
            Rect outer = new Rect(0f, 0f, inRect.width, inRect.height);
            Rect view = new Rect(0f, 0f, contentW, ContentH);
            Widgets.BeginScrollView(outer, ref outerScroll, view);

            float x = 6f;
            float w = contentW - 12f;
            float y = 4f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, w - 220f, 26f), "剧情事件编辑器（_user_events.json 即时写盘，可滚动）");
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(x + w - 210f, y, 100f, 26f), "新建"))
            {
                sel = null;
                idF = "my_event";
                titleF = ""; openingF = ""; npcF = "cast"; triggerF = "interval";
                cooldownF = "24"; intMinF = "8"; intMaxF = "24"; winF = "24"; maxTimesF = "99";
                affMinF = "-101"; affMaxF = "101"; flagF = "";
                nextF = ""; doneFlagsF = ""; loreF = ""; initialF = true;
                status = "新建：填写后点“保存为我的条目”。";
            }
            if (Widgets.ButtonText(new Rect(x + w - 100f, y, 90f, 26f), "关闭"))
            {
                Close();
                Widgets.EndScrollView();
                return;
            }
            y += 32f;

            Rect listRect = new Rect(x, y, w, 150f);
            var defs = new List<StoryEventDef>(EventStore.All);
            Widgets.BeginScrollView(listRect, ref listScroll, new Rect(0f, 0f, w - 16f, defs.Count * 26f + 6f));
            for (int i = 0; i < defs.Count; i++)
            {
                StoryEventDef d = defs[i];
                Rect row = new Rect(0f, 4f + i * 26f, w - 16f, 22f);
                if (Widgets.ButtonText(row, (sel == d ? "★ " : "") + d.id + "（" + d.title + "）[" + (IsUser(d) ? "我" : "文件") + "]"))
                {
                    sel = d;
                    LoadFrom(d);
                }
            }
            Widgets.EndScrollView();
            y += 156f;

            if (sel != null && !IsUser(sel))
            {
                GUI.color = new Color(1f, 0.85f, 0.5f);
                Widgets.Label(new Rect(x, y, w, 20f), "该事件来自文件（只读）。保存时会按你填的 id 创建一份我的条目（不改原文件）。");
                GUI.color = Color.white;
                y += 24f;
            }

            y = Row(x, y, w, "ID", ref idF, true);
            y = Row(x, y, w, "标题", ref titleF, true);
            y = Row(x, y, w, "NPC 选择", ref npcF, false, NpcNames);
            y = Row(x, y, w, "触发类型", ref triggerF, false, TriggerNames);
            y = NumRow(x, y, w, "冷却(h)", "间隔min(h)", ref cooldownF, ref intMinF);
            y = NumRow(x, y, w, "间隔max(h)", "对话窗口(h)", ref intMaxF, ref winF);
            y = NumRow(x, y, w, "次数上限", "好感min", ref maxTimesF, ref affMinF);
            y = NumRow(x, y, w, "好感max", "flag名(flagSet)", ref affMaxF, ref flagF);
            y = Row(x, y, w, "完成后解锁 nextIds(逗号)", ref nextF, true);
            y = Row(x, y, w, "完成置flag setFlagsOnDone(逗号)", ref doneFlagsF, true);
            y = Row(x, y, w, "预激活世界书 loreKeys(逗号)", ref loreF, true);
            bool init = initialF;
            Widgets.CheckboxLabeled(new Rect(x, y, w, 24f), "初始解锁 initial", ref init);
            initialF = init;
            y += 30f;

            Widgets.Label(new Rect(x, y, w, 18f), "开场注 opening（对话围绕它展开）");
            y += 20f;
            openingF = Widgets.TextArea(new Rect(x, y, w, 90f), openingF);
            y += 96f;

            if (Widgets.ButtonText(new Rect(x, y, 220f, 32f), "保存为我的条目"))
            {
                Save();
            }
            if (sel != null && IsUser(sel) && Widgets.ButtonText(new Rect(x + 230f, y, 190f, 32f), "删除我的条目"))
            {
                EventStore.DeleteUser(sel.id);
                status = "已删除：" + sel.id;
                sel = null;
            }
            y += 42f;
            GUI.color = new Color(1f, 0.9f, 0.6f);
            Widgets.Label(new Rect(x, y, w, 60f), status);
            GUI.color = Color.white;

            Widgets.EndScrollView();
        }

        private void LoadFrom(StoryEventDef d)
        {
            idF = d.id;
            titleF = d.title;
            openingF = d.opening;
            npcF = d.npcSelector;
            triggerF = d.trigger == EvTriggerKind.AfterDialogue ? "afterDialogue"
                : d.trigger == EvTriggerKind.FlagSet ? "flagSet"
                : d.trigger == EvTriggerKind.Affinity ? "affinity" : "interval";
            cooldownF = d.cooldownHours.ToString();
            intMinF = d.intervalMinHours.ToString();
            intMaxF = d.intervalMaxHours.ToString();
            winF = d.afterDialogueWindowHours.ToString();
            maxTimesF = d.maxTimes.ToString();
            affMinF = d.affinityMin.ToString();
            affMaxF = d.affinityMax.ToString();
            flagF = d.afterFlag;
            nextF = string.Join(",", d.nextIds);
            doneFlagsF = string.Join(",", d.setFlagsOnDone);
            loreF = string.Join(",", d.loreKeys);
            initialF = d.initial;
            status = "已载入：" + d.id;
        }

        private void Save()
        {
            string typedId = idF.Trim();
            if (typedId.Length == 0 || titleF.Trim().Length == 0)
            {
                status = "ID 与标题必填。";
                return;
            }
            StoryEventDef baseDef;
            if (sel != null && IsUser(sel))
            {
                baseDef = sel.Copy();
                baseDef.id = sel.id;
                status = "已保存为我的条目：" + sel.id;
            }
            else
            {
                baseDef = new StoryEventDef();
                baseDef.id = EventStore.MakeUniqueId(typedId);
                status = baseDef.id != typedId
                    ? "id 已存在，已改名为 " + baseDef.id + "。"
                    : "已保存为我的条目：" + baseDef.id;
            }
            baseDef.title = titleF.Trim();
            baseDef.opening = openingF.Trim();
            baseDef.npcSelector = npcF;
            baseDef.trigger = triggerF == "afterDialogue" ? EvTriggerKind.AfterDialogue
                : triggerF == "flagSet" ? EvTriggerKind.FlagSet
                : triggerF == "affinity" ? EvTriggerKind.Affinity : EvTriggerKind.Interval;
            baseDef.cooldownHours = Int(cooldownF, 24);
            baseDef.intervalMinHours = Int(intMinF, 8);
            baseDef.intervalMaxHours = Int(intMaxF, 24);
            baseDef.afterDialogueWindowHours = Int(winF, 24);
            baseDef.maxTimes = Int(maxTimesF, 99);
            baseDef.affinityMin = Int(affMinF, -101);
            baseDef.affinityMax = Int(affMaxF, 101);
            baseDef.afterFlag = flagF.Trim();
            baseDef.nextIds = Split(nextF);
            baseDef.setFlagsOnDone = Split(doneFlagsF);
            baseDef.loreKeys = Split(loreF);
            baseDef.initial = initialF;
            EventStore.UpsertUser(baseDef);
            RimTavernGameComp.Get()?.RefreshUnlockedEvents();
            status = "已保存并写盘：" + baseDef.id;
        }

        private static int Int(string s, int def)
        {
            return int.TryParse(s, out int v) ? v : def;
        }

        private static List<string> Split(string s)
        {
            var list = new List<string>();
            foreach (string part in s.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.Length > 0 && !list.Contains(t)) list.Add(t);
            }
            return list;
        }

        private float Row(float x, float y, float w, string label, ref string value, bool editable, string[] cycle = null)
        {
            if (cycle != null)
            {
                int idx = Array.IndexOf(cycle, value);
                idx = idx < 0 ? 0 : idx;
                Widgets.Label(new Rect(x, y, 200f, 24f), label);
                if (Widgets.ButtonText(new Rect(x + 205f, y, 160f, 24f), value))
                {
                    value = cycle[(idx + 1) % cycle.Length];
                }
                return y + 30f;
            }
            Widgets.Label(new Rect(x, y, 200f, 24f), label);
            GUI.enabled = editable;
            value = Widgets.TextField(new Rect(x + 205f, y, w - 205f, 24f), value);
            GUI.enabled = true;
            return y + 30f;
        }

        private float NumRow(float x, float y, float w, string l1, string l2, ref string v1, ref string v2)
        {
            Widgets.Label(new Rect(x, y, 190f, 24f), l1);
            v1 = Widgets.TextField(new Rect(x + 195f, y, 110f, 24f), v1);
            Widgets.Label(new Rect(x + 315f, y, 210f, 24f), l2);
            v2 = Widgets.TextField(new Rect(x + 530f, y, w - 530f, 24f), v2);
            return y + 30f;
        }
    }
}
