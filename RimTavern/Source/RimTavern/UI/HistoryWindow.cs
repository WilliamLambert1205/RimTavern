using System.Collections.Generic;
using RimTavern.Core;
using RimTavern.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTavern.UI
{
    /// <summary>Scrollable window with ALL archived dialogues (newest first, persisted in the save).
    /// Text wraps and row heights are measured with the configured overlay font size.</summary>
    public class HistoryWindow : Window
    {
        private readonly List<SavedDialogue> data;
        private Vector2 scroll;

        public HistoryWindow()
        {
            var comp = RimTavernGameComp.Get();
            data = comp != null ? comp.GetArchiveSnapshot() : new List<SavedDialogue>();
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
            closeOnCancel = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(820f, 640f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float pad = 10f;
            Rect area = inRect.ContractedBy(pad);
            float x = area.x;
            float w = area.width;
            float y = area.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, w, 28f), "全部历史对话（随存档保存，最多保留最近 60 场）");
            Text.Font = GameFont.Small;
            y += 34f;

            if (data.Count == 0)
            {
                Widgets.Label(new Rect(x, y, w, 24f), "（还没有记录过的对话。结束一场对话后会自动归档到这里。）");
                return;
            }

            var originalFont = Text.Font;
            var originalAnchor = Text.Anchor;
            var originalWrap = Text.WordWrap;
            var originalColor = GUI.color;
            int originalSize = Text.fontStyles[(int)Text.Font].fontSize;
            try
            {
                float font = RimTavernMod.Settings != null ? RimTavernMod.Settings.overlayFontSize : 14f;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.fontStyles[(int)Text.Font].fontSize = Mathf.Max(8, (int)font);
                Text.WordWrap = true;

                float textW = w - 34f; // room for scrollbar + indent
                float totalH = 6f;
                foreach (SavedDialogue d in data)
                {
                    totalH += 30f; // session header
                    foreach (SavedLine l in d.lines)
                    {
                        totalH += Text.CalcHeight(l.text, textW) + 3f;
                    }
                    totalH += 10f;
                }

                Widgets.BeginScrollView(new Rect(x, y, w, area.yMax - y), ref scroll,
                    new Rect(0f, 0f, w - 18f, totalH));

                float yy = 4f;
                foreach (SavedDialogue d in data)
                {
                    string date = DateAtTick(d.tick);
                    GUI.color = new Color(0.85f, 0.8f, 0.55f);
                    Widgets.Label(new Rect(2f, yy, w - 30f, 28f),
                        d.protoLabel + "  ↔  " + d.npcLabel + "　·　" + date + "　·　" + d.lines.Count + " 句");
                    GUI.color = Color.white;
                    yy += 30f;

                    foreach (SavedLine l in d.lines)
                    {
                        float rowH = Text.CalcHeight(l.text, textW) + 2f;
                        GUI.color = l.isNpc ? new Color(0.85f, 0.92f, 1f) : new Color(1f, 0.95f, 0.85f);
                        Widgets.Label(new Rect(14f, yy, textW, rowH),
                            (l.isNpc ? "" : "◇") + l.speaker + "： " + l.text);
                        GUI.color = Color.white;
                        yy += rowH;
                    }
                    yy += 10f;
                }

                Widgets.EndScrollView();
            }
            finally
            {
                Text.fontStyles[(int)Text.Font].fontSize = originalSize;
                Text.Font = originalFont;
                Text.Anchor = originalAnchor;
                Text.WordWrap = originalWrap;
                GUI.color = originalColor;
            }
        }

        private static string DateAtTick(int tick)
        {
            try
            {
                int tile = Find.CurrentMap != null ? Find.CurrentMap.Tile : 0;
                return GenDate.DateFullStringAt(tick, (Vector2)Find.WorldGrid.GetTileCenter(tile));
            }
            catch (System.Exception)
            {
                return "?";
            }
        }
    }
}
