using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimTavern.Data;
using RimTavern.Util;
using Verse;

namespace RimTavern.Logic
{
    public enum TalkStage
    {
        NpcThinking,
        OptionsThinking,
        AwaitChoice,
        Error,
        Ended
    }

    public class DialogueLine
    {
        public readonly string Speaker;
        public readonly string Text;
        public readonly bool IsNpc;

        public DialogueLine(string speaker, string text, bool isNpc)
        {
            Speaker = speaker;
            Text = text;
            IsNpc = isNpc;
        }
    }

    /// <summary>
    /// Galgame-style session state machine (M1, no worldbook/memory yet):
    ///   NPC turn (LLM) -> option generation (LLM) -> player picks an option or types free text -> repeat.
    /// All public methods must be called on the main thread. LLM work happens on
    /// background threads and never touches Unity objects (prompts are pre-built strings).
    /// </summary>
    public class DialogueSession
    {
        public readonly Pawn Protagonist;
        public readonly Pawn Npc;

        public List<DialogueLine> History { get; } = new List<DialogueLine>();
        public List<string> Options { get; private set; } = new List<string>();

        public TalkStage Stage { get; private set; } = TalkStage.NpcThinking;
        public string BusyHint { get; private set; } = "";
        public string ErrorText { get; private set; } = "";

        private readonly string protoName;
        private readonly string npcName;
        private readonly bool npcInitiated;
        private readonly string eventNote;
        private readonly List<string> forceLoreKeys = new List<string>();
        private readonly int protoId;
        private readonly int npcId;
        private string lastNpcInstruction = "";
        private string sceneBlock = "";
        private string sceneAppliedText = "";
        private IntVec3? lastFocusPos;
        private int lastSceneTick = -1;
        private bool summaryScheduled;
        private Task<string> pending;
        private bool pendingIsOptions;
        private string pendingTag = "";
        private CancellationTokenSource pendingCts;
        private bool disposed;
        private bool started;

        public DialogueSession(Pawn protagonist, Pawn npc, bool npcInitiated = false, string eventNote = "", List<string> forceLoreKeys = null)
        {
            Protagonist = protagonist;
            Npc = npc;
            this.npcInitiated = npcInitiated;
            this.eventNote = eventNote ?? "";
            if (forceLoreKeys != null) this.forceLoreKeys.AddRange(forceLoreKeys);
            protoName = protagonist?.LabelShort ?? "主角";
            npcName = npc?.LabelShort ?? "对方";
            protoId = protagonist != null ? protagonist.thingIDNumber : -1;
            npcId = npc != null ? npc.thingIDNumber : -1;
        }

        public bool NpcInitiated { get { return npcInitiated; } }

        public string ProtagonistName { get { return protoName; } }
        public string NpcName { get { return npcName; } }

        public bool Disposed { get { return disposed; } }

        public void Start()
        {
            if (started || disposed) return;
            started = true;
            RefreshScene();
            BeginNpcTurn("开场白");
        }

        public void PlayerSubmit(string text)
        {
            if (disposed || Stage != TalkStage.AwaitChoice) return;
            string t = text?.Trim();
            if (string.IsNullOrEmpty(t)) return;
            Options.Clear();
            History.Add(new DialogueLine(protoName, t, false));
            RefreshScene();
            BeginNpcTurn("回应主角刚才的话");
        }

        public void Retry()
        {
            if (disposed) return;
            ErrorText = "";
            Stage = TalkStage.NpcThinking;
            RefreshScene();
            BeginNpcTurn(lastNpcInstruction);
        }

        /// <summary>Swipe-style: roll a new set of options for the current NPC line.</summary>
        public void RegenerateOptions()
        {
            if (disposed || pending != null || Stage != TalkStage.AwaitChoice) return;
            Stage = TalkStage.OptionsThinking;
            BusyHint = "重新生成选项中…";
            StartCall(BuildOptionMessages(), 220, true, "options-gen");
        }

        /// <summary>Swipe-style: re-write the NPC's last reply (removes it and regenerates).</summary>
        public bool TryRegenerateLastNpc()
        {
            if (disposed || pending != null || Stage != TalkStage.AwaitChoice) return false;
            if (History.Count == 0 || !History[History.Count - 1].IsNpc) return false;
            History.RemoveAt(History.Count - 1);
            string inst = lastNpcInstruction.Length > 0 ? lastNpcInstruction : "回应主角刚才的话";
            RefreshScene();
            BeginNpcTurn(inst);
            return true;
        }

        public void EndSession()
        {
            if (disposed) return;
            CancelPending();
            Stage = TalkStage.Ended;
            ScheduleEndSummary();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelPending();
        }

        /// <summary>Must be called every UI frame on the main thread.</summary>
        public void Tick()
        {
            if (disposed || pending == null) return;
            if (!pending.IsCompleted) return;

            string result;
            bool ok;
            try
            {
                result = pending.Result;
                ok = true;
            }
            catch (Exception ex)
            {
                result = ex.Message;
                ok = false;
            }
            pending = null;
            pendingCts?.Dispose();
            pendingCts = null;

            if (!ok)
            {
                if (disposed) return;
                Stage = TalkStage.Error;
                ErrorText = result;
                Diag.Log("error|" + pendingTag, result);
                Log.Warning("[RimTavern] LLM call failed: " + result);
                return;
            }

            Diag.Log("resp|" + pendingTag, result);
            string doneTag = pendingTag;
            pendingTag = "";
            if (pendingIsOptions) ApplyOptions(result);
            else ApplyNpcLine(result);
        }

        // ---------- internals ----------

        /// <summary>
        /// Refresh the scene block only when something significant changed (start of talk, speaker
        /// moved far, or a long time passed) so the same nearby items are not re-mentioned every turn.
        /// </summary>
        private void RefreshScene()
        {
            try
            {
                Pawn f = Protagonist;
                bool mayRebuild = true;
                if (f != null && f.Spawned && lastFocusPos.HasValue)
                {
                    int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                    bool movedFar = f.Position.DistanceTo(lastFocusPos.Value) > 6f;
                    bool longGap = lastSceneTick >= 0 && (now - lastSceneTick) > 6000; // ~2.4 in-game hours
                    mayRebuild = movedFar || longGap;
                }
                if (!mayRebuild) return; // reuse cached block, do not re-inject

                if (f != null && f.Spawned)
                {
                    lastFocusPos = f.Position;
                    lastSceneTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                }
                sceneBlock = RimTavern.Perception.SceneBuilder.BuildSceneBlock(
                    RimTavern.Core.RimTavernGameComp.Get(), Protagonist, Npc);
                Diag.Log("scene|refresh", sceneBlock.Length == 0 ? "（重建后为空）" : sceneBlock);
            }
            catch (System.Exception ex)
            {
                sceneBlock = "";
                Diag.Log("error|scene", ex.Message);
                Log.Warning("[RimTavern] 场景感知刷新失败: " + ex.Message);
            }
        }

        /// <summary>Scene block is injected once per change (opening included), not on every turn.</summary>
        private bool ConsumeSceneIfNew()
        {
            if (sceneBlock.Length == 0) return false;
            if (sceneAppliedText == sceneBlock) return false;
            sceneAppliedText = sceneBlock;
            Diag.Log("inject|scene", "本次注入场景块（仅变化时注入一次）");
            return true;
        }

        private string SceneSearchText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(protoName).Append(' ').Append(npcName).Append(' ');
            if (sceneBlock.Length > 0) sb.Append(sceneBlock);
            // match against the whole recent history window (same as the prompt), not just the last lines
            int window = RimTavernMod.Settings != null ? RimTavernMod.Settings.maxHistoryPairs * 2 : 24;
            int start = History.Count - window;
            if (start < 0) start = 0;
            for (int i = start; i < History.Count; i++)
            {
                DialogueLine line = History[i];
                sb.Append(line.Speaker).Append(' ').Append(line.Text).Append(' ');
            }
            foreach (string k in forceLoreKeys)
            {
                if (!string.IsNullOrEmpty(k)) sb.Append(' ').Append(k);
            }
            return sb.ToString();
        }

        private void BeginNpcTurn(string instruction)
        {
            lastNpcInstruction = instruction;
            Stage = TalkStage.NpcThinking;
            BusyHint = instruction == "开场白" ? npcName + " 正在开口…" : npcName + " 正在回应…";
            List<LlmMessage> messages = BuildNpcMessages(instruction);
            StartCall(messages, 300, false, "npc-turn");
        }

        private void ApplyNpcLine(string raw)
        {
            string line = CleanNpcLine(raw);
            if (string.IsNullOrEmpty(line))
            {
                line = npcName + "欲言又止。";
            }
            History.Add(new DialogueLine(npcName, line, true));
            Diag.Log("parse|npc-line", line);
            Stage = TalkStage.OptionsThinking;
            BusyHint = "生成可选回应中…";
            StartCall(BuildOptionMessages(), 220, true, "options-gen");
        }

        private void ApplyOptions(string raw)
        {
            int want = RimTavernMod.Settings != null ? RimTavernMod.Settings.optionCount : 3;
            if (want < 2) want = 2;
            if (want > 5) want = 5;

            var parsed = new List<string>();
            JsonNode root = Json.Parse(raw);
            JsonArray arr = root?.GetArray("options");
            if (arr != null)
            {
                foreach (JsonNode n in arr.Items)
                {
                    string s = JsonNode.Str(n);
                    if (!string.IsNullOrWhiteSpace(s)) parsed.Add(s.Trim());
                }
            }

            var unique = new List<string>();
            foreach (string s in parsed)
            {
                if (unique.Count >= want) break;
                if (!unique.Contains(s)) unique.Add(s);
            }
            if (unique.Count == 0)
            {
                Log.Warning("[RimTavern] Option parse failed, using fallbacks. Raw: " + raw);
                unique.Add("（继续听他说下去）");
                unique.Add("（把话题引到别处）");
            }

            Options = unique;
            Stage = TalkStage.AwaitChoice;
            BusyHint = "";
        }

        private void StartCall(List<LlmMessage> messages, int maxTokens, bool isOptions, string tag)
        {
            if (disposed) return;
            CancelPending();
            pendingIsOptions = isOptions;
            pendingTag = tag;
            Diag.Log("req|" + tag, JoinMessages(messages));
            pendingCts = new CancellationTokenSource();
            CancellationToken ct = pendingCts.Token;
            var client = LlmClientFactory.Create();
            pending = Task.Run(() => client.Chat(messages, maxTokens, ct), ct);
        }

        private static string JoinMessages(List<LlmMessage> messages)
        {
            var sb = new System.Text.StringBuilder();
            if (messages == null) return "(空)";
            foreach (LlmMessage m in messages)
            {
                sb.Append("──").Append(m.role).Append("──\n").Append(m.content).Append('\n');
            }
            return sb.ToString();
        }

        private void CancelPending()
        {
            try
            {
                pendingCts?.Cancel();
            }
            catch (Exception) { }
            pendingCts?.Dispose();
            pendingCts = null;
            pending = null;
        }

        private static string CleanNpcLine(string raw)
        {
            if (raw == null) return "";
            string s = raw.Trim();
            // strip surrounding quotes and asterisks
            if (s.Length >= 2 &&
                ((s[0] == '"' && s[s.Length - 1] == '"') ||
                 (s[0] == '“' && s[s.Length - 1] == '”') ||
                 (s[0] == '「' && s[s.Length - 1] == '」')))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s;
        }

        private List<LlmMessage> BuildNpcMessages(string instruction)
        {
            var list = new List<LlmMessage>();
            list.Add(new LlmMessage("system", BuildNpcSystem()));
            AddHistory(list);
            list.Add(new LlmMessage("user", "（" + instruction + "）现在轮到你（" + npcName + "）说话，直接以第一人称说出你的台词："));
            return list;
        }

        private List<LlmMessage> BuildOptionMessages()
        {
            var list = new List<LlmMessage>();
            string sys = "你是 RimWorld 的剧情辅助，为主角生成可选台词。" +
                         "\n主角：" + protoName + "；对方：" + npcName + "。";
            try
            {
                string protoCard = RimTavern.Logic.ContextAssembler.SpeakerBlock(Protagonist, "主角", false, 450);
                if (protoCard.Length > 0) sys += "\n" + protoCard;
                string lore = RimTavern.Logic.ContextAssembler.ActiveLoreBlock(SceneSearchText(),
                    RimTavernMod.Settings != null ? RimTavernMod.Settings.loreBudgetChars : 500);
                if (lore.Length > 0) sys += "\n" + lore;
                string memRecall = MemoryRecallText(2, 300);
                if (memRecall.Length > 0) sys += "\n" + memRecall;
            }
            catch (Exception ex) { Log.Warning("[RimTavern] 选项上下文组装失败: " + ex.Message); }
            sys += "\n请站在主角立场，针对对话最近一句，生成风格各异（直接/温和/试探/拒绝/转移话题等）的" +
                   (RimTavernMod.Settings != null ? RimTavernMod.Settings.optionCount.ToString() : "3") + "条短台词。" +
                   "\n只输出一个 JSON 对象 {\"options\":[\"…\",\"…\"]}，不要输出任何其他文字、解释或引号。";
            list.Add(new LlmMessage("system", sys));
            AddHistory(list);
            list.Add(new LlmMessage("user", "（" + npcName + "刚说完上面的话，现在为主角生成可选的下一句台词。）"));
            return list;
        }

        private string BuildNpcSystem()
        {
            bool opening = History.Count == 0; // 开场语只在第一轮出现，否则模型每轮都"重新开场"导致复读
            string scene = opening
                ? (npcInitiated
                    ? npcName + "主动来找主角攀谈，由对方先开口。"
                    : "是主角主动找" + npcName + "攀谈。")
                : "你们正在继续这段谈话。";

            var sb = new System.Text.StringBuilder();
            sb.Append("你是 RimWorld 世界中的角色「").Append(npcName).Append("」。");
            try
            {
                // authored card (if any) + dynamic state; style examples steer the NPC voice
                string npcCard = RimTavern.Logic.ContextAssembler.SpeakerBlock(Npc, npcName + "（你）", true, 650);
                if (npcCard.Length > 0) sb.Append('\n').Append(npcCard);
                string protoCard = RimTavern.Logic.ContextAssembler.SpeakerBlock(Protagonist, "主角", false, 400);
                if (protoCard.Length > 0) sb.Append("\n与主角交谈，主角参考：").Append(protoCard);

                // chapter note (author-note slot, P3-5 minimal)
                string ch = ChapterNoteText();
                if (ch.Length > 0) sb.Append('\n').Append(ch);

                string lore = RimTavern.Logic.ContextAssembler.ActiveLoreBlock(SceneSearchText(),
                    RimTavernMod.Settings != null ? RimTavernMod.Settings.loreBudgetChars : 500);
                if (lore.Length > 0) sb.Append('\n').Append(lore);

                string memRecall = MemoryRecallText(
                    RimTavernMod.Settings != null ? RimTavernMod.Settings.memMaxLines : 3,
                    RimTavernMod.Settings != null ? RimTavernMod.Settings.memBudgetChars : 400);
                if (memRecall.Length > 0) sb.Append('\n').Append(memRecall);

                sb.Append("\n当前场景：").Append(scene);
                if (eventNote.Length > 0)
                {
                    sb.Append("\n事件起因（对话必须围绕它展开）：").Append(eventNote);
                }
                if (ConsumeSceneIfNew())
                {
                    sb.Append("\n现场感知（只能引用其中出现的名字，不能虚构其中的东西）：\n").Append(sceneBlock);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimTavern] NPC 上下文组装失败: " + ex.Message);
            }

            sb.Append("\n规则：\n1 只用中文，以第一人称、以该角色的身份说话；\n" +
                      "2 每次回复 1~3 句，自然口语，不要长篇；\n" +
                      "3 禁止括号动作、旁白、内心描写、引号，直接说出台词；\n" +
                      "4 语气与角色性格、固定性格设定与说话风格示范一致；\n" +
                      "5 只能提及对话/背景中真实存在的人物与事情，不得编造陌生人或事件；\n" +
                      "6 不得复读或复述自己或主角已经说过的话；\n" +
                      "7 严禁逐字照抄角色卡示范、背景、世界书等任何注引文字的原句——只模仿语气与口癖，说话必须用自己的话组织。");
            return sb.ToString();
        }

        private static string ChapterNoteText()
        {
            var s = RimTavernMod.Settings;
            if (s == null) return "";
            string title = (s.chapterTitle ?? "").Trim();
            string note = (s.chapterNote ?? "").Trim();
            if (title.Length == 0 && note.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("【当前章节】");
            if (title.Length > 0) sb.Append(title);
            if (note.Length > 0) sb.Append(" 目标/氛围：").Append(note);
            return sb.ToString();
        }

        private void AddHistory(List<LlmMessage> list)
        {
            int maxPairs = RimTavernMod.Settings != null ? RimTavernMod.Settings.maxHistoryPairs : 12;
            int start = History.Count - maxPairs * 2;
            if (start < 0) start = 0;
            for (int i = start; i < History.Count; i++)
            {
                DialogueLine line = History[i];
                list.Add(new LlmMessage("user", "（" + line.Speaker + "）" + line.Text));
            }
        }

        // ---------------- P4: memory recall + dialogue-end summary ----------------

        private string MemoryRecallText(int maxLines, int maxChars)
        {
            try
            {
                var comp = RimTavern.Core.RimTavernGameComp.Get();
                return comp != null ? comp.MemoryPromptText(npcId, maxLines, maxChars) : "";
            }
            catch (System.Exception)
            {
                return "";
            }
        }

        private void ScheduleEndSummary()
        {
            if (summaryScheduled || disposed) return;
            summaryScheduled = true;
            if (History.Count < 2 || protoId <= 0 || npcId <= 0) return;
            var s = RimTavernMod.Settings;
            if (s == null || !s.memoryEnabled) return;

            var lines = new List<string>();
            int start = History.Count - 20;
            if (start < 0) start = 0;
            for (int i = start; i < History.Count; i++)
            {
                DialogueLine line = History[i];
                lines.Add("（" + line.Speaker + "）" + line.Text);
            }
            string pLbl = protoName;
            string nLbl = npcName;
            int nId = npcId;

            Task.Run(delegate
            {
                try
                {
                    var client = LlmClientFactory.Create();
                    var msgs = new List<LlmMessage>
                    {
                        new LlmMessage("system",
                            "你是 RimWorld 剧情书记官。分析主角 " + pLbl + " 与 " + nLbl + " 的这段对话（仅限该对话内容）。" +
                            "只输出一个 JSON 对象：{\"affinityDelta\":(整数，-2~+2),\"summary\":\"30~80字中文摘要\"}。不要任何其他文字。"),
                        new LlmMessage("user", string.Join("\n", lines))
                    };
                    string raw = client.Chat(msgs, 220);
                    JsonNode root = Json.Parse(raw);
                    int delta = 0;
                    string summary = "";
                    if (root != null)
                    {
                        double d = root.GetNum("affinityDelta", 0);
                        delta = (int)Math.Round(d);
                        if (delta < -3) delta = -3;
                        if (delta > 3) delta = 3;
                        summary = root.GetStr("summary", "") ?? "";
                    }
                    RimTavern.Core.RimTavernGameComp.EnqueueOutcome(new MemoryOutcome
                    {
                        npcId = nId,
                        affinityDelta = delta,
                        summary = summary
                    });
                }
                catch (System.Exception)
                {
                    // still record the interaction tick with zero delta
                    RimTavern.Core.RimTavernGameComp.EnqueueOutcome(new MemoryOutcome { npcId = nId });
                }
            });
        }
    }
}
