using UnityEngine;
using Verse;

namespace RimTavern.UI
{
    /// <summary>
    /// RimWorld 1.6 translucent dialogue-record overlay.
    ///
    /// 修复/实现要点（2026-09-05 三轮）：
    ///  - 唯一绘制+输入通道 = 引擎自动调用的 MapComponentOnGUI()（事件链最前段，
    ///    每个 GUI 事件 pass 都执行；不要放到 UIRoot Postfix——那时事件早被消费）。
    ///  - 窗口矩形 winRect 是“跨 pass 持续”的实例状态：只在空闲（未拖/未缩）时从设置读取，
    ///    拖动期间不被重置（这是拖拽/缩放此前完全不生效的根因——每 pass 重读设置把增量冲掉了）。
    ///  - 拖拽/缩放输入 = 事件 + Input 轮询混合：MouseDrag 事件驱动，另用每帧
    ///    Input.GetMouseButton/mousePosition 兜底，保证不依赖具体事件投递顺序。
    ///  - 字号滑条只在内存里改值即时生效，鼠标松开后才写一次设置（避免拖动时高频落盘卡顿）。
    ///  - 模态窗口（forcePause/absorbInputAroundWindow，如 Dialog_ModSettings）打开时整窗让路。
    /// </summary>
    public class TavernHistoryOverlay : MapComponent
    {
        private static bool loggedAttach;

        // 跨 pass 持续的窗口矩形：拖动期间累加增量，不再每 pass 从设置重读
        private Rect winRect;
        private bool dragging;
        private bool resizing;
        private Vector2 dragOffset;
        private Vector2 lastMousePos;
        private bool showFontPanel;
        private bool fontDirty;      // 字号已改、待落盘（鼠标松开后写一次）

        private const float HeaderH = 28f;
        private const float ButtonsBlockW = 212f; // Aa(44)+6+全部历史(82)+6+收起(74)
        private const float FontPanelW = 190f;
        private const float FontPanelH = 76f;

        public TavernHistoryOverlay(Map map) : base(map)
        {
            if (!loggedAttach)
            {
                loggedAttach = true;
                Log.Message("[RimTavern] Overlay MapComponent 已挂载到地图（构造器执行）。");
            }
        }

        public override void MapComponentOnGUI()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing) return;
                if (Find.CurrentMap != map) return;
                var s = RimTavernMod.Settings;
                if (s == null || !s.overlayEnabled) return;

                if (AnyModalWindowOpen())
                {
                    dragging = false;
                    resizing = false;
                    showFontPanel = false;
                    fontDirty = false;
                    return;
                }

                if (TavernHistory.Ring.Count == 0)
                {
                    TavernHistory.SetFromArchive(40);
                }

                // 只在空闲时同步设置里的矩形；拖动/缩放期间保留增量（关键修复）
                if (!dragging && !resizing)
                {
                    winRect = s.GetOverlayRect();
                }
                ClampRectToScreen(ref winRect);

                HandleWindowInput(s);

                Widgets.DrawBoxSolid(winRect, new Color(0f, 0f, 0f, 0.5f));
                Widgets.DrawBoxSolidWithOutline(winRect, new Color(0f, 0f, 0f, 0.5f),
                    new Color(1f, 1f, 1f, 0.32f), 1);

                var originalFont = Text.Font;
                var originalAnchor = Text.Anchor;
                var originalWrap = Text.WordWrap;
                var originalColor = GUI.color;
                int originalSmallSize = Text.fontStyles[(int)GameFont.Small].fontSize;
                try
                {
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.fontStyles[(int)GameFont.Small].fontSize =
                        Mathf.Max(8, Mathf.RoundToInt(s.overlayFontSize > 0f ? s.overlayFontSize : 14f));
                    Text.WordWrap = true;

                    float pad = 6f;
                    Rect inner = winRect.ContractedBy(pad);

                    Rect titleRect;
                    Rect aaRect;
                    Rect allRect;
                    Rect colRect;
                    ComputeHeaderRects(winRect, out titleRect, out aaRect, out allRect, out colRect);

                    GUI.color = new Color(0.85f, 0.85f, 0.85f);
                    Widgets.Label(titleRect, "对话记录（拖标题移动 · 右下角缩放）");
                    GUI.color = Color.white;

                    if (Widgets.ButtonText(aaRect, "Aa"))
                    {
                        showFontPanel = !showFontPanel;
                    }
                    TooltipHandler.TipRegion(aaRect, "字号");
                    if (Widgets.ButtonText(allRect, "全部历史"))
                    {
                        Find.WindowStack.Add(new HistoryWindow());
                    }
                    if (Widgets.ButtonText(colRect, "收起"))
                    {
                        s.overlayEnabled = false;
                        s.Write();
                        return;
                    }

                    float listTop = inner.y + HeaderH + 2f;
                    float listBottom = inner.yMax - pad;
                    float nameW = 100f;
                    float textX = inner.x + nameW + 4f;
                    float textW = inner.xMax - pad - textX;
                    if (textW < 60f) textW = 60f;

                    int shown = 0;
                    for (int i = TavernHistory.Ring.Count - 1; i >= 0; i--)
                    {
                        OvlLine line = TavernHistory.Ring[i];
                        float rowH = Text.CalcHeight(line.text, textW) + 2f;
                        if (listTop + rowH > listBottom) break;
                        GUI.color = line.isNpc ? new Color(0.72f, 0.86f, 1f) : new Color(1f, 0.9f, 0.6f);
                        Widgets.Label(new Rect(inner.x, listTop, nameW, rowH), line.speaker);
                        GUI.color = Color.white;
                        Widgets.Label(new Rect(textX, listTop, textW, rowH), line.text);
                        listTop += rowH;
                        shown++;
                        if (shown >= 100) break;
                    }
                    if (shown == 0)
                    {
                        GUI.color = new Color(0.9f, 0.9f, 0.9f);
                        Widgets.Label(new Rect(inner.x, listTop, inner.width, 22f),
                            "（暂无——结束一场对话后这里会出现记录）");
                    }

                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Widgets.DrawTextureFitted(new Rect(winRect.xMax - 18f, winRect.yMax - 18f, 14f, 14f),
                        TexUI.WinExpandWidget, 1f);
                    GUI.color = Color.white;

                    if (showFontPanel)
                    {
                        DrawFontPanel(FontPanelRect(winRect), s);
                    }

                    // 字号滑条改动只在“松开鼠标”后落盘一次，拖动过程只改内存（防卡顿）
                    if (fontDirty && !Input.GetMouseButton(0))
                    {
                        fontDirty = false;
                        s.Write();
                    }
                }
                finally
                {
                    Text.fontStyles[(int)GameFont.Small].fontSize = originalSmallSize;
                    Text.Font = originalFont;
                    Text.Anchor = originalAnchor;
                    Text.WordWrap = originalWrap;
                    GUI.color = originalColor;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("[RimTavern] 悬浮窗绘制失败: " + ex.Message);
            }
        }

        // ---------------- 输入：拖拽 / 缩放（事件 + 轮询混合） ----------------

        private void HandleWindowInput(RimTavernSettings s)
        {
            Event e = Event.current;
            if (e == null) return;
            Vector2 mouse = e.mousePosition;
            bool down = Input.GetMouseButton(0);

            Rect resizeRect = new Rect(winRect.xMax - 26f, winRect.yMax - 26f, 26f, 26f);
            Rect titleRect, aaRect, allRect, colRect;
            ComputeHeaderRects(winRect, out titleRect, out aaRect, out allRect, out colRect);

            // 字号面板开着时：点面板/按钮内部交给控件；点外面关闭
            if (showFontPanel)
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    Rect panel = FontPanelRect(winRect);
                    if (panel.Contains(mouse) || aaRect.Contains(mouse) || allRect.Contains(mouse) || colRect.Contains(mouse))
                        return;
                    showFontPanel = false;
                    fontDirty = false;
                    e.Use();
                }
                return;
            }

            if (!down)
            {
                // 松开鼠标：结束拖/缩并落盘一次
                if (dragging || resizing)
                {
                    dragging = false;
                    resizing = false;
                    s.SetOverlayRect(winRect);
                    s.Write();
                }
                return;
            }

            // —— 按下状态 ——
            if (!dragging && !resizing)
            {
                // 只在本帧“刚按下”时启动，避免鼠标从窗口外拖进来误触发
                if (Input.GetMouseButtonDown(0))
                {
                    if (resizeRect.Contains(mouse))
                    {
                        resizing = true;
                    }
                    else if (titleRect.Contains(mouse))
                    {
                        dragging = true;
                        dragOffset = mouse - new Vector2(winRect.x, winRect.y);
                    }
                    if ((dragging || resizing) && e.type == EventType.MouseDown && e.button == 0)
                    {
                        e.Use();
                    }
                }
                lastMousePos = mouse;
                return;
            }

            // —— 拖动/缩放中：事件增量 + 每帧轮询增量（不依赖具体事件顺序） ——
            Vector2 d = mouse - lastMousePos;
            if (dragging)
            {
                winRect.x += d.x;
                winRect.y += d.y;
            }
            else if (resizing)
            {
                winRect.width = Mathf.Clamp(winRect.width + d.x, 320f, Mathf.Max(320f, Verse.UI.screenWidth - winRect.x));
                winRect.height = Mathf.Clamp(winRect.height + d.y, 140f, Mathf.Max(140f, Verse.UI.screenHeight - winRect.y));
            }
            if (e.type == EventType.MouseDrag)
            {
                e.Use();
            }
            lastMousePos = mouse;
            ClampRectToScreen(ref winRect);
        }

        // ---------------- 字号面板 ----------------

        private void DrawFontPanel(Rect rect, RimTavernSettings s)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.95f));
            var listing = new Listing_Standard();
            listing.Begin(rect.ContractedBy(8f));
            listing.Label("字号: " + Mathf.RoundToInt(s.overlayFontSize).ToString());
            float v = listing.Slider(s.overlayFontSize, 10f, 22f);
            int rounded = Mathf.RoundToInt(v);
            if (rounded != Mathf.RoundToInt(s.overlayFontSize))
            {
                s.overlayFontSize = rounded;   // 只改内存，即时生效
                fontDirty = true;              // 落盘推迟到松开鼠标（见 MapComponentOnGUI 末尾）
            }
            listing.End();
        }

        private Rect FontPanelRect(Rect anchor)
        {
            float x = Mathf.Clamp(anchor.xMax - FontPanelW, 0f, Mathf.Max(0f, Verse.UI.screenWidth - FontPanelW));
            float y = anchor.yMax + 4f;
            if (y + FontPanelH > Verse.UI.screenHeight)
            {
                y = anchor.y - FontPanelH - 4f;
            }
            return new Rect(x, y, FontPanelW, FontPanelH);
        }

        private static void ComputeHeaderRects(Rect winRect, out Rect titleRect,
            out Rect aaRect, out Rect allRect, out Rect colRect)
        {
            Rect inner = winRect.ContractedBy(6f);
            float y = inner.y;
            float h = HeaderH - 6f;

            aaRect = new Rect(inner.xMax - ButtonsBlockW, y, 44f, h);
            allRect = new Rect(aaRect.xMax + 6f, y, 82f, h);
            colRect = new Rect(allRect.xMax + 6f, y, 74f, h);

            float titleEnd = aaRect.x - 6f;
            float titleW = titleEnd - inner.x;
            if (titleW < 40f) titleW = 40f;
            titleRect = new Rect(inner.x, y, titleW, h);
        }

        private static bool AnyModalWindowOpen()
        {
            var ws = Find.WindowStack;
            if (ws == null) return false;
            foreach (Window w in ws.Windows)
            {
                if (w == null) continue;
                if (w.forcePause || w.absorbInputAroundWindow) return true;
            }
            return false;
        }

        private static void ClampRectToScreen(ref Rect r)
        {
            float sw = Verse.UI.screenWidth;
            float sh = Verse.UI.screenHeight;
            if (r.x < 0f) r.x = 0f;
            if (r.y < 0f) r.y = 0f;
            if (r.x + r.width > sw) r.x = Mathf.Max(0f, sw - r.width);
            if (r.y + r.height > sh) r.y = Mathf.Max(0f, sh - r.height);
            if (r.width < 320f) r.width = 320f;
            if (r.height < 140f) r.height = 140f;
        }
    }
}
