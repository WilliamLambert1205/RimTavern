using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using RimTavern.Core;
using RimTavern.Data;
using RimTavern.UI;
using RimTavern.Util;
using UnityEngine;
using Verse;

namespace RimTavern
{
    /// <summary>Mod entry: settings (scrollable), Harmony patching, author-content loading.</summary>
    public class RimTavernMod : Mod
    {
        public static RimTavernSettings Settings { get; private set; }

        private static string testStatus = "";
        private static bool testRunning;
        private static string storyMsg = "";
        private static string evMsg = "";
        private static string contentStatus = "";
        private static string settingsError = "";
        private static string copyStatus = "";
        private static Vector2 settingsScroll;

        public RimTavernMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimTavernSettings>();
            new Harmony("ChihayaAllin.RimTavern").PatchAll(Assembly.GetExecutingAssembly());

            try
            {
                string cardsDir = Path.Combine(content.RootDir, "Cards");
                string wbDir = Path.Combine(content.RootDir, "WorldBooks");
                string evDir = Path.Combine(content.RootDir, "Events");
                Directory.CreateDirectory(cardsDir);
                Directory.CreateDirectory(wbDir);
                Directory.CreateDirectory(evDir);
                ContentStore.Init(cardsDir, wbDir);
                RimTavern.Data.EventStore.Init(evDir);
            }
            catch (Exception ex)
            {
                Log.Error("[RimTavern] 无法初始化内容目录（Cards/WorldBooks）: " + ex.Message);
            }

            Log.Message("[RimTavern] loaded. Dialogue engine M1+M2, P1 content store active, P2 scene builder active.");
        }

        public override string SettingsCategory() => "RimTavern";

        // ------------------------------------------------------------------
        // Scrollable settings. The previous Listing layout had no scrollbar, so
        // the bottom "角色卡与世界书" section was silently clipped below the
        // visible area — that hid the per-colonist [分配]/[编辑卡] buttons and
        // the main-menu hint.
        // ------------------------------------------------------------------
        public override void DoSettingsWindowContents(Rect inRect)
        {
            try
            {
                if (Settings == null) return;

                float contentW = inRect.width - 24f;
                float totalH = Mathf.Max(Layout(contentW, 0f, true), inRect.height);

                Rect outer = new Rect(0f, 0f, inRect.width, inRect.height);
                Rect view = new Rect(0f, 0f, contentW, totalH);
                Widgets.BeginScrollView(outer, ref settingsScroll, view);

                Layout(contentW, 0f, false);
                Settings.Write();

                Widgets.EndScrollView();
                settingsError = "";
            }
            catch (Exception ex)
            {
                Log.Error("[RimTavern] 设置页绘制异常（请把此行日志发给我）: " + ex);
                settingsError = ex.Message;
            }

            if (settingsError.Length > 0)
            {
                GUI.color = new Color(1f, 0.6f, 0.6f);
                Widgets.Label(new Rect(inRect.x + 8f, inRect.yMax - 70f, inRect.width - 16f, 60f),
                    "RimTavern 设置页出错：" + settingsError);
                GUI.color = Color.white;
            }
        }

        // ------------------------------------------------------------------
        // One layout function for both measure (dry) and draw. Row heights are
        // fixed so both passes advance identically; GUI is only touched when
        // !dry. Returns total content height.
        // ------------------------------------------------------------------
        private float Layout(float w, float y0, bool dry)
        {
            float x = 6f;
            float y = y0;
            var s = Settings;
            var comp = RimTavernGameComp.Get();

            // ----- LLM interface -----
            y = SectionLabel(x, y, w, dry, "LLM 接口（OpenAI 兼容：OpenAI / DeepSeek / Ollama / vLLM 等）");
            y = TextFieldRow(x, y, w, dry, "Base URL", ref s.baseUrl);
            y = TextFieldRow(x, y, w, dry, "API Key", ref s.apiKey);
            y = TextFieldRow(x, y, w, dry, "Model", ref s.model);

            if (dry) y += RowH;
            else
            {
                Widgets.Label(new Rect(x, y, 240f, 26f), "Temperature: " + s.temperature.ToString("0.00"));
                s.temperature = Widgets.HorizontalSlider(new Rect(x + 250f, y, w - 260f, 26f), s.temperature, 0f, 1.5f);
            }
            y += RowH;

            if (dry) y += RowH;
            else
            {
                Widgets.Label(new Rect(x, y, 260f, 26f), "每次生成选项数: " + s.optionCount);
                s.optionCount = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(x + 270f, y, w - 280f, 26f), s.optionCount, 2f, 5f, false, null, null, null, 1f));
            }
            y += RowH;

            if (dry) y += RowH;
            else
            {
                Widgets.Label(new Rect(x, y, 260f, 26f), "场景感知预算（字符）: " + s.sceneBudgetChars);
                s.sceneBudgetChars = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(x + 270f, y, w - 280f, 26f), s.sceneBudgetChars, 300f, 2000f, false, null, null, null, 50f));
            }
            y += RowH;

            bool diag = s.logDiagnostics;
            if (!dry) Widgets.CheckboxLabeled(new Rect(x, y, w, 26f), "日志诊断输出（prompt/结果）", ref diag);
            s.logDiagnostics = diag;
            y += RowH;

            if (!dry && Widgets.ButtonText(new Rect(x, y, 320f, 32f), "测试连接（向模型发送一句问候）") && !testRunning)
            {
                testRunning = true;
                testStatus = "连接测试中…（最长等待 3 分钟）";
                Thread t = new Thread(delegate ()
                {
                    string err;
                    string reply = Logic.LlmClientFactory.QuickChat("你好，请用一句话确认你在线。", out err);
                    Diag.Log("test|prompt", "你好，请用一句话确认你在线。");
                    Diag.Log("test|resp", reply != null ? reply : ("失败：" + err));
                    testStatus = reply != null ? ("成功：" + reply) : ("失败：" + err);
                    testRunning = false;
                });
                t.IsBackground = true;
                t.Start();
            }
            y += 38f;
            if (!dry && testStatus.Length > 0)
            {
                Widgets.Label(new Rect(x, y, w, 22f), testStatus);
            }
            y += 24f;

            // ----- protagonist -----
            if (dry) y += 24f;
            else
            {
                if (comp == null || !comp.TryGetProtagonist(out Pawn proto, false))
                {
                    Widgets.Label(new Rect(x, y, w, 22f), "当前主角：未指定（选中殖民者后右键其自身可设为故事主角）");
                }
                else
                {
                    Widgets.Label(new Rect(x, y, w, 22f), "当前主角：" + proto.LabelCap);
                    if (Widgets.ButtonText(new Rect(x + w - 140f, y, 140f, 26f), "清除主角指定"))
                    {
                        comp.ClearProtagonist();
                    }
                }
            }
            y += 30f;

            // ----- scripted events -----
            y = SectionLabel(x, y, w, dry, "剧本事件（配角自动找主角）");
            bool ev = s.storyEventsEnabled;
            if (!dry) Widgets.CheckboxLabeled(new Rect(x, y, w, 26f), "启用自动剧本事件弹窗", ref ev);
            s.storyEventsEnabled = ev;
            y += RowH;

            if (dry) y += RowH;
            else
            {
                Widgets.Label(new Rect(x, y, 300f, 26f), "事件间隔（游戏小时）: " + s.storyEventIntervalHours);
                s.storyEventIntervalHours = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(x + 310f, y, w - 320f, 26f), s.storyEventIntervalHours, 2f, 72f, false, null, null, null, 1f));
            }
            y += RowH;

            if (!dry && Widgets.ButtonText(new Rect(x, y, 260f, 32f), "立即触发一次（调试）"))
            {
                var c2 = RimTavernGameComp.Get();
                storyMsg = (c2 != null && c2.TryOpenAutoStoryDialogue(true))
                    ? "已尝试触发剧本事件。"
                    : "触发失败：需要主角可用，且其地图上存在可对话的人形角色。";
            }
            y += 38f;
            if (!dry && storyMsg.Length > 0)
            {
                Widgets.Label(new Rect(x, y, w, 22f), storyMsg);
            }
            y += 24f;

            // P5 events
            if (!dry)
            {
                Widgets.Label(new Rect(x, y, w, 22f), "P5 事件定义：" + RimTavern.Data.EventStore.Count + " 条（Events/）");
                y += 26f;
                if (Widgets.ButtonText(new Rect(x, y, 320f, 30f), "强制触发下一个可用事件（调试）"))
                {
                    var c3 = RimTavernGameComp.Get();
                    string m = "";
                    bool ok = c3 != null && c3.TryFireNextEvent(true, out m);
                    evMsg = ok ? "已触发：" + m : "未触发：" + (c3 == null ? "无存档组件" : m);
                }
                if (Widgets.ButtonText(new Rect(x + 340f, y, 260f, 30f), "管理事件定义（编辑器）"))
                {
                    Find.WindowStack.Add(new RimTavern.UI.EventEditorWindow());
                }
                y += 36f;
                if (evMsg.Length > 0) Widgets.Label(new Rect(x, y, w, 22f), evMsg);
                y += 24f;
            }
            else
            {
                y += 26f + 36f + 24f;
            }

            // ----- chapter note (author-note slot, P3-5 minimal) -----
            y = SectionLabel(x, y, w, dry, "章节状态（作者笔记式注入，可留空）");
            y = TextFieldRow(x, y, w, dry, "章节标题", ref s.chapterTitle);
            y = TextFieldRow(x, y, w, dry, "当前目标/氛围", ref s.chapterNote);

            // ----- memory & affinity (P4) -----
            y = SectionLabel(x, y, w, dry, "记忆与好感（P4：对话收尾摘要 + 隐式好感）");
            bool mem = s.memoryEnabled;
            if (!dry) Widgets.CheckboxLabeled(new Rect(x, y, w, 26f), "启用对话记忆（结束后生成摘要并记录好感变化）", ref mem);
            s.memoryEnabled = mem;
            y += RowH;
            if (dry) y += RowH;
            else
            {
                Widgets.Label(new Rect(x, y, 300f, 26f), "每次回查记忆条数: " + s.memMaxLines);
                s.memMaxLines = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(x + 310f, y, w - 320f, 26f), s.memMaxLines, 1f, 6f, false, null, null, null, 1f));
            }
            y += RowH;
            if (dry) y += RowH;
            else
            {
                Widgets.Label(new Rect(x, y, 300f, 26f), "记忆预算（字符）: " + s.memBudgetChars);
                s.memBudgetChars = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(x + 310f, y, w - 320f, 26f), s.memBudgetChars, 200f, 900f, false, null, null, null, 50f));
            }
            y += RowH;

            // ----- history overlay -----
            y = SectionLabel(x, y, w, dry, "界面（历史对话悬浮窗：可拖动标题栏/右下角缩放，记录随存档保存）");
            bool ov = s.overlayEnabled;
            if (!dry) Widgets.CheckboxLabeled(new Rect(x, y, w, 26f), "显示对话记录悬浮窗", ref ov);
            s.overlayEnabled = ov;
            y += RowH;
            if (dry) y += RowH;
            else
            {
                Widgets.Label(new Rect(x, y, 300f, 26f), "显示行数: " + s.overlayMaxLines);
                s.overlayMaxLines = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(x + 310f, y, w - 320f, 26f), s.overlayMaxLines, 3f, 16f, false, null, null, null, 1f));
            }
            y += RowH;
            if (dry) y += RowH;
            else
            {
                Widgets.Label(new Rect(x, y, 300f, 26f), "字号: " + s.overlayFontSize.ToString("0"));
                s.overlayFontSize = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(x + 310f, y, w - 320f, 26f), s.overlayFontSize, 10f, 22f, false, null, null, null, 1f));
            }
            y += RowH;
            if (!dry)
            {
                if (Widgets.ButtonText(new Rect(x, y, 220f, 30f), "开启悬浮窗（若已关闭）"))
                {
                    s.overlayEnabled = true;
                }
                if (Widgets.ButtonText(new Rect(x + 230f, y, 240f, 30f), "打开全部历史对话窗口"))
                {
                    Find.WindowStack.Add(new RimTavern.UI.HistoryWindow());
                }
            }
            y += 34f;

            // ----- diagnostics log -----
            y = SectionLabel(x, y, w, dry, "调试日志（记录上传给模型的完整提示/模型原文/场景·卡·世界书注入）");
            if (!dry)
            {
                Widgets.Label(new Rect(x, y, w - 340f, 26f),
                    "缓冲 " + Diag.Count + " 条（上限 150，满则丢最旧）");
                if (Widgets.ButtonText(new Rect(x + w - 330f, y, 160f, 26f), "复制调试日志"))
                {
                    string text = Diag.FormatRecent();
                    GUIUtility.systemCopyBuffer = text;
                    copyStatus = "已复制 " + text.Length + " 字符到剪贴板（请粘贴到外部文本编辑器查看）。";
                }
                if (Widgets.ButtonText(new Rect(x + w - 160f, y, 90f, 26f), "清空"))
                {
                    Diag.Clear();
                    copyStatus = "已清空日志缓冲。";
                }
            }
            y += 30f;
            if (!dry && copyStatus.Length > 0)
            {
                Widgets.Label(new Rect(x, y, w, 22f), copyStatus);
            }
            y += 24f;

            // ----- cards & worldbooks -----
            y = SectionLabel(x, y, w, dry, "角色卡与世界书（Mod 文件夹 Cards / WorldBooks，改动即时写盘）");
            y = ContentButtons(x, y, w, dry);
            if (!dry && contentStatus.Length > 0)
            {
                Widgets.Label(new Rect(x, y, w, 22f), contentStatus);
            }
            y += 24f;

            if (!dry && Widgets.ButtonText(new Rect(x, y, 240f, 30f), "管理世界书条目（P3-2）"))
            {
                Find.WindowStack.Add(new WorldbookEditorWindow());
            }
            y += 34f;

            if (Current.Game == null)
            {
                if (!dry)
                {
                    Widgets.Label(new Rect(x, y, w, 44f),
                        "当前在主菜单：只能查看/重载内容文件。角色卡的分配与编辑需要进入存档（进入后回到本页滚动到此处操作）。");
                }
                return y + 48f;
            }
            if (Find.Maps == null || Find.Maps.Count == 0)
            {
                if (!dry)
                {
                    Widgets.Label(new Rect(x, y, w, 44f),
                        "当前存档没有地图/殖民者：进入殖民地后回到本页即可对殖民者编辑/分配角色卡。");
                }
                return y + 48f;
            }

            var pawns = CollectColonists();
            if (pawns.Count == 0)
            {
                if (!dry) Widgets.Label(new Rect(x, y, w, 22f), "（暂无可用殖民者）");
                return y + 26f;
            }

            foreach (Pawn p in pawns)
            {
                if (dry)
                {
                    y += RowH;
                    continue;
                }
                float rowY = y;
                try
                {
                    y = PawnRow(x, y, w, comp, p);
                }
                catch (Exception ex)
                {
                    Log.Error("[RimTavern] 殖民者行绘制异常 (" + p.LabelShort + "): " + ex.Message);
                    y = rowY + RowH;
                }
            }
            return y + 12f;
        }

        // ---------- layout helpers (fixed heights, dry-safe) ----------

        private const float RowH = 30f;

        private float SectionLabel(float x, float y, float w, bool dry, string text)
        {
            if (!dry)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(x, y, w, 28f), text);
                Text.Font = GameFont.Small;
            }
            return y + 32f;
        }

        private float TextFieldRow(float x, float y, float w, bool dry, string label, ref string value)
        {
            if (!dry)
            {
                float labelW = Mathf.Min(200f, w * 0.32f);
                Widgets.Label(new Rect(x, y, labelW, 24f), label);
                value = Widgets.TextField(new Rect(x + labelW, y, w - labelW, 24f), value);
            }
            return y + 28f;
        }

        private float ContentButtons(float x, float y, float w, bool dry)
        {
            if (!dry)
            {
                if (Widgets.ButtonText(new Rect(x, y, 260f, 30f), "重新加载内容文件"))
                {
                    try
                    {
                        ContentStore.LoadAll();
                        RimTavern.Data.EventStore.LoadAll();
                        RimTavern.Core.RimTavernGameComp.Get()?.RefreshUnlockedEvents();
                        contentStatus = "已重新加载（打开中的编辑窗口请关闭后重开以刷新）。";
                        settingsError = "";
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[RimTavern] 内容重载失败: " + ex);
                        contentStatus = "重载出错：" + ex.Message;
                    }
                }
                Widgets.Label(new Rect(x + 280f, y, w - 280f, 30f),
                    "已加载：角色卡 " + ContentStore.CardCount + " 张 / 世界书条目 " + ContentStore.LoreCount + " 条");
            }
            return y + 34f;
        }

        private List<Pawn> CollectColonists()
        {
            var pawns = new List<Pawn>();
            var seen = new HashSet<int>();
            if (Find.Maps == null) return pawns;
            for (int m = 0; m < Find.Maps.Count; m++)
            {
                List<Pawn> all = Find.Maps[m].mapPawns.AllPawns;
                if (all == null) continue;
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p == null || p.Dead || !p.RaceProps.Humanlike || !p.IsFreeColonist) continue;
                    if (!seen.Add(p.thingIDNumber)) continue;
                    pawns.Add(p);
                }
            }
            return pawns;
        }

        private float PawnRow(float x, float y, float w, RimTavernGameComp comp, Pawn p)
        {
            string boundUid = comp.GetBoundCardUid(p.thingIDNumber);
            bool hasBound = boundUid != null && ContentStore.GetCard(boundUid) != null;
            bool hasNameCard = !hasBound && ContentStore.FindForPawn(p) != null;

            string tag = (comp.IsCast(p) ? "[配角] " : "") + p.LabelShort;
            tag += hasBound ? "（已绑定）" : (hasNameCard ? "（名字匹配）" : "（自动卡）");

            Widgets.Label(new Rect(x, y, w - 220f, 28f), tag);
            if (Widgets.ButtonText(new Rect(x + w - 212f, y, 100f, 28f), "编辑卡"))
            {
                Find.WindowStack.Add(new CharCardEditorWindow(p));
            }
            if (Widgets.ButtonText(new Rect(x + w - 104f, y, 98f, 28f), "分配"))
            {
                Find.WindowStack.Add(new CharCardAssignWindow(p));
            }
            return y + RowH;
        }
    }
}
