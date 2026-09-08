using RimTavern.Core;
using RimTavern.Data;
using UnityEngine;
using Verse;

namespace RimTavern.UI
{
    /// <summary>
    /// P1-5 + review fixes:
    /// - Editing an ST-imported card NEVER writes the import file: saving creates a user copy
    ///   (_card_*) and binds this pawn to the copy.
    /// - A pawn's effective card = save-game binding first, legacy name match second, auto last.
    /// - Auto layer stays runtime-only and is never persisted.
    /// </summary>
    public class CharCardEditorWindow : Window
    {
        private readonly Pawn pawn;
        private string personality = "";
        private string speech = "";
        private string scenario = "";
        private string status = "";
        private readonly string note = "";
        private readonly string baseCardName;

        public CharCardEditorWindow(Pawn pawn)
        {
            this.pawn = pawn;

            var comp = RimTavernGameComp.Get();
            CharacterCard eff = null;
            if (comp != null && pawn != null)
            {
                string bound = comp.GetBoundCardUid(pawn.thingIDNumber);
                if (bound != null) eff = ContentStore.GetCard(bound);
            }
            if (eff == null) eff = ContentStore.FindForPawn(pawn); // legacy name fallback

            personality = eff?.Personality ?? "";
            speech = eff?.SpeechExamples ?? "";
            scenario = eff?.Scenario ?? "";
            baseCardName = eff != null && !string.IsNullOrEmpty(eff.Name)
                ? eff.Name
                : ContentStore.DefaultPawnCardName(pawn);

            if (eff != null)
            {
                note = "当前卡片：" + eff.Name + "（" + eff.OriginLabel + "）";
                if (!eff.IsUserOwned)
                {
                    note += "\n这是导入卡：保存时会创建本 Mod 副本并使用副本，原导入文件不会被改动。";
                }
            }
            else
            {
                note = "当前无作者卡，将使用运行时自动生成层。留空直接关闭即可。";
            }

            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(640f, 660f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float pad = 12f;
            Rect area = inRect.ContractedBy(pad);
            float x = area.x;
            float w = area.width;
            float y = area.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, w, 26f), "角色卡编辑：" + (pawn?.LabelShort ?? "?"));
            Text.Font = GameFont.Small;
            y += 30f;

            GUI.color = new Color(0.8f, 0.85f, 1f);
            Widgets.Label(new Rect(x, y, w, 44f), note);
            GUI.color = Color.white;
            y += 48f;

            y = Field(x, y, w, 140f, "性格要点（想固定下来的性格/口头禅方向）", ref personality);
            y = Field(x, y, w, 100f, "说话范例（2~3 句示范风格，可选）", ref speech);
            y = Field(x, y, w, 90f, "附加设定（背景/秘密等，可选）", ref scenario);
            y += 6f;

            if (Widgets.ButtonText(new Rect(x, y, 190f, 32f), "保存并绑定到本人"))
            {
                TrySave();
            }
            if (Widgets.ButtonText(new Rect(x + 200f, y, 200f, 32f), "恢复自动（删除副本/解除）"))
            {
                TryDelete();
            }
            if (Widgets.ButtonText(new Rect(x + w - 80f, y, 80f, 32f), "关闭"))
            {
                Close();
            }
            y += 40f;

            GUI.color = new Color(1f, 0.9f, 0.6f);
            Widgets.Label(new Rect(x, y, w, 44f), status);
            GUI.color = Color.white;
        }

        private void TrySave()
        {
            try
            {
                bool wasImported = false;
                CharacterCard eff = ContentStore.GetCard(RimTavernGameComp.Get()?.GetBoundCardUid(pawn.thingIDNumber));
                if (eff == null) eff = ContentStore.FindForPawn(pawn);
                if (eff != null && !eff.IsUserOwned) wasImported = true;

                string name = baseCardName;
                CharacterCard card = ContentStore.SaveUserCard(name, personality, speech, scenario);
                RimTavernGameComp.Get()?.SetCardBinding(pawn.thingIDNumber, card.Uid);
                status = wasImported
                    ? "已创建用户副本并绑定（原导入文件未改动）。"
                    : "已保存并绑定到本人（自动层不受影响）。";
            }
            catch (System.Exception ex)
            {
                status = "保存失败：" + ex.Message;
            }
        }

        private void TryDelete()
        {
            var comp = RimTavernGameComp.Get();
            if (comp == null) return;
            string uid = comp.GetBoundCardUid(pawn.thingIDNumber);
            if (uid != null)
            {
                if (ContentStore.IsUserUid(uid)) ContentStore.DeleteUserCard(uid);
                comp.ClearCardBinding(pawn.thingIDNumber);
            }
            personality = "";
            speech = "";
            scenario = "";
            status = "已删除我的副本 / 解除绑定，恢复自动生成。";
        }

        private float Field(float x, float y, float w, float h, string title, ref string value)
        {
            Widgets.Label(new Rect(x, y, w, 20f), title);
            y += 22f;
            value = Widgets.TextArea(new Rect(x, y, w, h), value);
            return y + h + 8f;
        }
    }
}
