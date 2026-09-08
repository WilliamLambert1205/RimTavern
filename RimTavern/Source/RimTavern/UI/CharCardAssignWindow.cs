using RimTavern.Core;
using RimTavern.Data;
using UnityEngine;
using Verse;

namespace RimTavern.UI
{
    /// <summary>
    /// Assign any loaded character card to a pawn (protagonist / cast / any colonist), or unbind
    /// to fall back to legacy name-matching / auto card. Bindings live in the save game and are
    /// adjustable anytime.
    /// </summary>
    public class CharCardAssignWindow : Window
    {
        private readonly Pawn pawn;
        private readonly string currentUid;
        private string status = "";

        public CharCardAssignWindow(Pawn pawn)
        {
            this.pawn = pawn;
            var comp = RimTavernGameComp.Get();
            currentUid = comp?.GetBoundCardUid(pawn.thingIDNumber);
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(560f, 560f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float pad = 12f;
            Rect area = inRect.ContractedBy(pad);
            float x = area.x;
            float w = area.width;
            float y = area.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, w, 26f), "分配角色卡：" + (pawn?.LabelShort ?? "?"));
            Text.Font = GameFont.Small;
            y += 32f;

            // "None / auto" option
            bool currentIsNone = string.IsNullOrEmpty(currentUid);
            string noneLabel = currentIsNone ? "★ （自动生成 / 不绑定）" : "（自动生成 / 不绑定）";
            if (Widgets.ButtonText(new Rect(x, y, w, 30f), noneLabel))
            {
                RimTavernGameComp.Get()?.ClearCardBinding(pawn.thingIDNumber);
                status = "已解除绑定（无作者卡时将使用自动层；重名卡仍按名字匹配）。";
            }
            y += 36f;

            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(new Rect(x, y, w, 20f), "可用角色卡（已载入）：");
            GUI.color = Color.white;
            y += 24f;

            foreach (CharacterCard card in ContentStore.AllCards)
            {
                bool isSel = card.Uid == currentUid;
                string marker = isSel ? "★ " : "";
                string preview = card.Personality.Length > 38 ? card.Personality.Substring(0, 38) + "…" : card.Personality;
                string row = marker + card.Name + "（" + card.OriginLabel + "）";
                if (Widgets.ButtonText(new Rect(x, y, w, 30f), row))
                {
                    RimTavernGameComp.Get()?.SetCardBinding(pawn.thingIDNumber, card.Uid);
                    status = "已绑定：" + card.Name + "。";
                }
                TooltipHandler.TipRegion(new Rect(x, y, w, 30f), preview);
                y += 34f;
                if (y > area.yMax - 60f) break;
            }

            y = area.yMax - 52f;
            GUI.color = new Color(1f, 0.9f, 0.6f);
            Widgets.Label(new Rect(x, y, w - 90f, 40f), status);
            GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(x + w - 80f, y, 80f, 32f), "完成"))
            {
                Close();
            }
        }
    }
}
