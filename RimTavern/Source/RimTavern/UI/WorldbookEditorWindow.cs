using System;
using System.Collections.Generic;
using RimTavern.Data;
using UnityEngine;
using Verse;

namespace RimTavern.UI
{
    /// <summary>
    /// P3-2 minimal in-game worldbook editor.
    /// - Entries created here persist to the mod's "_user.worldinfo.json" immediately.
    /// - Imported (file) entries are read-only in memory: editing them offers "copy as mine".
    /// </summary>
    public class WorldbookEditorWindow : Window
    {
        private Vector2 listScroll;
        private LoreEntry sel;
        private string title = "";
        private string keysTxt = "";
        private string comment = "";
        private string content = "";
        private bool constant;
        private string status = "选择左侧条目编辑，或点“新建”。";

        public WorldbookEditorWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 620f); }
        }

        private static bool IsUser(LoreEntry e)
        {
            return e != null && (e.Source == ContentStore.InGameTag || e.Source == ContentStore.UserLoreFilePath());
        }

        public override void DoWindowContents(Rect inRect)
        {
            float pad = 10f;
            float x = inRect.x + pad;
            float w = inRect.width - pad * 2f;
            float y = inRect.y + pad;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, w - 220f, 26f), "世界书条目管理（改动即时写盘到 _user.worldinfo.json）");
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(x + w - 210f, y, 100f, 26f), "新建"))
            {
                sel = null;
                title = "";
                keysTxt = "";
                comment = "";
                content = "";
                constant = false;
                status = "正在新建条目。";
            }
            if (Widgets.ButtonText(new Rect(x + w - 100f, y, 90f, 26f), "关闭"))
            {
                Close();
                return;
            }
            y += 32f;

            // entry list (scroll)
            Rect listRect = new Rect(x, y, w, 150f);
            var entries = new List<LoreEntry>(ContentStore.Lore);
            float rowH = 30f;
            Widgets.BeginScrollView(listRect, ref listScroll, new Rect(0f, 0f, w - 18f, entries.Count * rowH + 6f));
            for (int i = 0; i < entries.Count; i++)
            {
                LoreEntry e = entries[i];
                Rect row = new Rect(0f, 4f + i * rowH, w - 18f, 26f);
                bool isSel = sel == e;
                string flag = e.Constant ? "[常开]" : "[词]";
                string src = IsUser(e) ? "我" : "文件";
                string label = flag + " " + (e.Title.Length > 0 ? e.Title : "(无标题)") + " | " + e.KeySummary + "（" + src + "）";
                if (isSel) GUI.color = new Color(0.5f, 0.7f, 1f);
                if (Widgets.ButtonText(row, label))
                {
                    sel = e;
                    LoadFrom(e);
                }
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();
            y += 156f;

            // editor fields
            if (sel != null && !IsUser(sel))
            {
                GUI.color = new Color(1f, 0.85f, 0.5f);
                Widgets.Label(new Rect(x, y, w, 20f),
                    "该条目来自 Mod 文件夹文件（只读）。修改前请点“复制为我的条目”。");
                GUI.color = Color.white;
                y += 24f;
            }

            y = FieldRow(x, y, w, "标题", ref title, true);
            y = FieldRow(x, y, w, "关键词（逗号分隔）", ref keysTxt, true);
            y = FieldRow(x, y, w, "备注（可选）", ref comment, true);
            Widgets.CheckboxLabeled(new Rect(x, y, w, 24f), "常开注入（不依赖关键词）", ref constant);
            y += 28f;

            Widgets.Label(new Rect(x, y, w, 18f), "内容（1~4 句，过长会被预算截断）");
            y += 20f;
            content = Widgets.TextArea(new Rect(x, y, w, 100f), content);
            y += 106f;

            if (Widgets.ButtonText(new Rect(x, y, 180f, 30f), "保存（即时写盘）"))
            {
                Save(false);
            }
            if (sel != null && !IsUser(sel) && Widgets.ButtonText(new Rect(x + 190f, y, 180f, 30f), "复制为我的条目"))
            {
                Save(true);
            }
            if (sel != null && IsUser(sel) && Widgets.ButtonText(new Rect(x + 190f, y, 160f, 30f), "删除本条目"))
            {
                ContentStore.DeleteLore(sel.Uid);
                status = "已删除：" + sel.Title + "。";
                sel = null;
                title = "";
                keysTxt = "";
                comment = "";
                content = "";
                constant = false;
            }
            y += 38f;
            GUI.color = new Color(1f, 0.9f, 0.6f);
            Widgets.Label(new Rect(x, y, w, 24f), status);
            GUI.color = Color.white;
        }

        private void LoadFrom(LoreEntry e)
        {
            title = e.Title;
            keysTxt = string.Join("、", e.Keys);
            comment = e.Comment;
            content = e.Content;
            constant = e.Constant;
            status = "已载入：" + title + "。";
        }

        private void Save(bool forceCopy)
        {
            string t = title.Trim();
            if (t.Length == 0 && content.Trim().Length == 0)
            {
                status = "标题与内容都为空，未保存。";
                return;
            }
            var keys = new List<string>();
            foreach (string part in keysTxt.Split(new[] { '、', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string k = part.Trim();
                if (k.Length > 0 && !keys.Contains(k)) keys.Add(k);
            }
            LoreEntry result;
            if (sel == null || forceCopy)
            {
                result = ContentStore.UpsertInGameLore(null, t, content.Trim(), constant, keys, comment.Trim());
                status = "已创建我的条目：" + t + "。";
            }
            else
            {
                result = ContentStore.UpsertInGameLore(sel.Uid, t, content.Trim(), constant, keys, comment.Trim());
                status = "已更新：" + t + "。";
            }
            sel = result;
        }

        private float FieldRow(float x, float y, float w, string label, ref string value, bool multiline)
        {
            Widgets.Label(new Rect(x, y, 150f, 24f), label);
            value = Widgets.TextField(new Rect(x + 155f, y, w - 155f, 24f), value);
            return y + 30f;
        }
    }
}
