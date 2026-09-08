using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace RimTavern.Data
{
    /// <summary>
    /// P1-5/P1-6: author content store (cards + worldbooks).
    /// Cards:
    ///  - RimTavern/Cards/*.chara.json (chara_card_v2). Files starting "_card_" are user-authored
    ///    copies written by this mod; other files are treated as ST imports and are NEVER overwritten
    ///    (editing an import creates a user copy instead).
    ///  - Each card has a stable Uid ("user:&lt;nameKey&gt;" / "file:&lt;fileName&gt;"). Binding a card
    ///    to a pawn (protagonist / cast / any pawn) lives in the save game (RimTavernGameComp), so
    ///    assignments are adjustable and survive reloads.
    /// Worldbooks:
    ///  - RimTavern/WorldBooks/*.worldinfo.json (ST world_info subset import).
    /// Edits are written to disk IMMEDIATELY. Auto default cards (CharacterCard.BuildAuto) are
    /// runtime-only and never persisted, so auto defaults can never overwrite player content.
    /// </summary>
    public static class ContentStore
    {
        public static string CardsDir = "";
        public static string WorldBooksDir = "";

        public const string UserUidPrefix = "user:";
        public const string FileUidPrefix = "file:";
        public const string UserCardFilePrefix = "_card_";

        private static readonly Dictionary<string, CharacterCard> cardsByUid =
            new Dictionary<string, CharacterCard>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CharacterCard> cardsByName =
            new Dictionary<string, CharacterCard>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<LoreEntry> loreEntries = new List<LoreEntry>();

        public static int CardCount { get { return cardsByUid.Count; } }
        public static int LoreCount { get { return loreEntries.Count; } }

        public static IEnumerable<CharacterCard> AllCards { get { return cardsByUid.Values; } }

        /// <summary>Read-only view used by later layers (P3 matcher).</summary>
        public static IReadOnlyList<LoreEntry> Lore { get { return loreEntries; } }

        public static void Init(string cardsDir, string worldBooksDir)
        {
            CardsDir = cardsDir ?? "";
            WorldBooksDir = worldBooksDir ?? "";
            LoadAll();
        }

        public static void LoadAll()
        {
            cardsByUid.Clear();
            cardsByName.Clear();
            loreEntries.Clear();
            LoadCards();
            LoadLore();
            DedupeLore();
            Log.Message("[RimTavern] 内容已加载：角色卡 " + cardsByUid.Count + " 张，世界书条目 " + loreEntries.Count + " 条。");
        }

        /// <summary>
        /// Remove duplicates that arise when the same book exists both as an imported file and as
        /// in-game user entries (same title+content). Keeps the first occurrence.
        /// </summary>
        private static void DedupeLore()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = loreEntries.Count - 1; i >= 0; i--)
            {
                LoreEntry e = loreEntries[i];
                if (e == null) continue;
                string key = (e.Title ?? "") + "|" + (e.Content ?? "");
                if (!seen.Add(key)) loreEntries.RemoveAt(i);
            }
        }

        // ---------------- cards ----------------

        private static void LoadCards()
        {
            if (string.IsNullOrEmpty(CardsDir) || !Directory.Exists(CardsDir)) return;
            foreach (string file in Directory.GetFiles(CardsDir, "*.chara.json"))
            {
                try
                {
                    string fileName = Path.GetFileName(file);
                    bool userOwned = fileName.StartsWith(UserCardFilePrefix, StringComparison.OrdinalIgnoreCase);
                    CharacterCard card = CharacterCard.ParseCharaV2(File.ReadAllText(file));
                    string nameKey = CharacterCard.NormalizeName(card.Name);
                    card.Uid = userOwned ? (UserUidPrefix + nameKey) : (FileUidPrefix + fileName.ToLowerInvariant());
                    card.IsUserOwned = userOwned;
                    card.SourceFile = file;
                    if (nameKey.Length == 0 && !userOwned)
                    {
                        Log.Warning("[RimTavern] 跳过无名字的导入卡: " + file);
                        continue;
                    }
                    if (!cardsByUid.ContainsKey(card.Uid)) cardsByUid[card.Uid] = card;
                    if (nameKey.Length > 0) cardsByName[nameKey] = card; // legacy name fallback; bindings take priority
                }
                catch (Exception ex)
                {
                    Log.Warning("[RimTavern] 角色卡解析失败 " + file + ": " + ex.Message);
                }
            }
        }

        /// <summary>Legacy convenience: first card whose name matches this pawn (used when no binding exists).</summary>
        public static CharacterCard FindForPawn(Pawn p)
        {
            if (p == null) return null;
            if (p.Name != null)
            {
                string full = CharacterCard.NormalizeName(p.Name.ToStringFull);
                if (full.Length > 0 && cardsByName.TryGetValue(full, out CharacterCard c)) return c;
            }
            string shortL = CharacterCard.NormalizeName(p.LabelShort);
            if (shortL.Length > 0 && cardsByName.TryGetValue(shortL, out CharacterCard c2)) return c2;
            string label = CharacterCard.NormalizeName(p.Label);
            if (label.Length > 0 && cardsByName.TryGetValue(label, out CharacterCard c3)) return c3;
            return null;
        }

        public static CharacterCard GetCard(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            return cardsByUid.TryGetValue(uid, out CharacterCard c) ? c : null;
        }

        public static bool IsUserUid(string uid)
        {
            return !string.IsNullOrEmpty(uid) && uid.StartsWith(UserUidPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string DefaultPawnCardName(Pawn p)
        {
            if (p == null) return "";
            if (p.Name != null) return p.Name.ToStringFull;
            return p.LabelShort;
        }

        /// <summary>
        /// Create (or overwrite) the USER copy of a card and return it. This is the ONLY write path
        /// for cards; ST-imported files are never modified. The returned card should then be bound to
        /// a pawn (or protagonist/cast) via RimTavernGameComp.SetCardBinding.
        /// </summary>
        public static CharacterCard SaveUserCard(string cardName, string personality, string speechExamples, string scenario)
        {
            string trimmedName = (cardName ?? "").Trim();
            if (trimmedName.Length == 0) trimmedName = "unnamed";
            string trimmedP = (personality ?? "").Trim();
            string trimmedS = (speechExamples ?? "").Trim();
            string trimmedSc = (scenario ?? "").Trim();
            if (trimmedP.Length == 0 && trimmedS.Length == 0 && trimmedSc.Length == 0)
            {
                throw new InvalidOperationException("[RimTavern] 空卡不保存，请直接使用“恢复自动”解除绑定。");
            }

            string nameKey = CharacterCard.NormalizeName(trimmedName);
            string uid = UserUidPrefix + nameKey;
            var card = new CharacterCard
            {
                Uid = uid,
                Name = trimmedName,
                Personality = trimmedP,
                SpeechExamples = trimmedS,
                Scenario = trimmedSc,
                IsUserOwned = true
            };

            try
            {
                if (string.IsNullOrEmpty(CardsDir)) Directory.CreateDirectory(CardsDir);
                else Directory.CreateDirectory(CardsDir);
                string path = Path.Combine(CardsDir, UserCardFilePrefix + SanitizeFileName(trimmedName) + ".chara.json");
                File.WriteAllText(path, card.ToCharaV2Json(), Encoding.UTF8);
                card.SourceFile = path;
                cardsByUid[uid] = card;
                cardsByName[nameKey] = card;
                Log.Message("[RimTavern] 用户角色卡已保存: " + path);
            }
            catch (Exception ex)
            {
                Log.Error("[RimTavern] 角色卡保存失败（目录只读？）: " + ex.Message);
            }
            return card;
        }

        /// <summary>Delete a user-owned card (file + indexes). Imported cards are never deleted here.</summary>
        public static void DeleteUserCard(string uid)
        {
            if (string.IsNullOrEmpty(uid) || !IsUserUid(uid)) return;
            CharacterCard card = GetCard(uid);
            if (card == null) return;
            cardsByUid.Remove(uid);
            string nameKey = CharacterCard.NormalizeName(card.Name);
            if (cardsByName.TryGetValue(nameKey, out CharacterCard byName) && byName.Uid == uid)
            {
                cardsByName.Remove(nameKey);
            }
            try
            {
                if (!string.IsNullOrEmpty(card.SourceFile) && File.Exists(card.SourceFile))
                {
                    File.Delete(card.SourceFile);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimTavern] 删除角色卡文件失败: " + ex.Message);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            string s = sb.ToString();
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        // ---------------- worldbooks (P1-6 infra; editing UI is P3-2) ----------------

        private static void LoadLore()
        {
            if (string.IsNullOrEmpty(WorldBooksDir) || !Directory.Exists(WorldBooksDir)) return;
            foreach (string file in Directory.GetFiles(WorldBooksDir, "*.worldinfo.json"))
            {
                try
                {
                    List<LoreEntry> parsed = LoreEntryIO.ParseWorldInfoJson(File.ReadAllText(file), file);
                    loreEntries.AddRange(parsed);
                }
                catch (Exception ex)
                {
                    Log.Warning("[RimTavern] 世界书解析失败 " + file + ": " + ex.Message);
                }
            }
        }

        /// <summary>Marker for entries authored in-game (persisted to the _user worldinfo file).</summary>
        public const string InGameTag = "<in-game>";

        public static string UserLoreFilePath()
        {
            return Path.Combine(WorldBooksDir, "_user.worldinfo.json");
        }

        private static bool IsUserEntry(LoreEntry e)
        {
            return e != null && (e.Source == InGameTag || e.Source == UserLoreFilePath());
        }

        /// <summary>Create (or update) an in-game authored worldbook entry and write to disk now.</summary>
        public static LoreEntry UpsertInGameLore(string uid, string title, string content,
            bool constant, List<string> keys, string comment)
        {
            LoreEntry existing = null;
            foreach (LoreEntry e in loreEntries)
            {
                if (e.Uid == uid) { existing = e; break; }
            }
            if (existing == null)
            {
                existing = new LoreEntry
                {
                    Uid = string.IsNullOrEmpty(uid)
                        ? "u" + DateTime.UtcNow.Ticks.ToString("x")
                        : uid
                };
                loreEntries.Add(existing);
            }
            existing.Title = title ?? "";
            existing.Content = content ?? "";
            existing.Constant = constant;
            existing.Keys = keys ?? new List<string>();
            existing.Comment = comment ?? "";
            existing.Enabled = true;
            existing.Source = InGameTag;
            PersistUserLore();
            return existing;
        }

        /// <summary>Remove a worldbook entry and persist its source file immediately.</summary>
        public static void DeleteLore(string uid)
        {
            LoreEntry entry = null;
            foreach (LoreEntry e in loreEntries)
            {
                if (e.Uid == uid) { entry = e; break; }
            }
            if (entry == null) return;

            bool user = IsUserEntry(entry);
            loreEntries.Remove(entry);

            if (user)
            {
                PersistUserLore();
            }
            else if (!string.IsNullOrEmpty(entry.Source) && File.Exists(entry.Source))
            {
                PersistFile(entry.Source, loreEntries.Where(e => e.Source == entry.Source).ToList());
            }
        }

        private static void PersistUserLore()
        {
            try
            {
                if (string.IsNullOrEmpty(WorldBooksDir)) Directory.CreateDirectory(WorldBooksDir);
                else Directory.CreateDirectory(WorldBooksDir);
                List<LoreEntry> user = loreEntries.Where(IsUserEntry).ToList();
                File.WriteAllText(UserLoreFilePath(), LoreEntryIO.ToJson(user), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Error("[RimTavern] 世界书（用户文件）保存失败（目录只读？）: " + ex.Message);
            }
        }

        private static void PersistFile(string path, List<LoreEntry> entries)
        {
            try
            {
                File.WriteAllText(path, LoreEntryIO.ToJson(entries), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Error("[RimTavern] 世界书文件写回失败: " + path + " — " + ex.Message);
            }
        }
    }
}
