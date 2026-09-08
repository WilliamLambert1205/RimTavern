using UnityEngine;
using Verse;

namespace RimTavern
{
    /// <summary>Mod settings: OpenAI-compatible endpoint + dialogue options.</summary>
    public class RimTavernSettings : ModSettings
    {
        // LLM endpoint (OpenAI-compatible: OpenAI / DeepSeek / Ollama / vLLM / LM Studio ...)
        public string baseUrl = "https://api.deepseek.com/v1";
        public string apiKey = "";
        public string model = "deepseek-chat";
        public float temperature = 0.9f;

        // Dialogue behaviour
        public int optionCount = 3;          // generated options per protagonist turn
        public int maxHistoryPairs = 12;     // recent (protagonist,npc) pairs fed to LLM
        public bool logDiagnostics = true;   // dump prompts/results to Player.log

        // M2: scripted (NPC-initiated) auto events
        public bool storyEventsEnabled = true;
        public int storyEventIntervalHours = 12; // in-game hours between auto story events

        // P2: scene perception
        public int sceneBudgetChars = 900;       // budget for the deterministic scene block

        // P3: lore (worldbook) injection budget
        public int loreBudgetChars = 500;

        // P3-5 (minimal): chapter note, injected author-note style
        public string chapterTitle = "";
        public string chapterNote = "";

        // P4: structured memory & implicit affinity
        public bool memoryEnabled = true;
        public int memMaxLines = 3;       // recalled memory lines per dialogue
        public int memBudgetChars = 400;

        // History overlay
        public bool overlayEnabled = true;
        public int overlayMaxLines = 8;
        public float overlayFontSize = 14f;
        public float overlayX = 14f;
        public float overlayY = 14f;
        public float overlayW = 580f;
        public float overlayH = 240f;

        public Rect GetOverlayRect()
        {
            float w = overlayW < 320f ? 320f : overlayW;
            float h = overlayH < 140f ? 140f : overlayH;
            return new Rect(overlayX, overlayY, w, h);
        }

        public void SetOverlayRect(Rect r)
        {
            overlayX = r.x;
            overlayY = r.y;
            overlayW = r.width;
            overlayH = r.height;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref baseUrl, "baseUrl", "https://api.deepseek.com/v1", false);
            Scribe_Values.Look(ref apiKey, "apiKey", "", false);
            Scribe_Values.Look(ref model, "model", "deepseek-chat", false);
            Scribe_Values.Look(ref temperature, "temperature", 0.9f, false);
            Scribe_Values.Look(ref optionCount, "optionCount", 3, false);
            Scribe_Values.Look(ref maxHistoryPairs, "maxHistoryPairs", 12, false);
            Scribe_Values.Look(ref logDiagnostics, "logDiagnostics", true, false);
            Scribe_Values.Look(ref storyEventsEnabled, "storyEventsEnabled", true, false);
            Scribe_Values.Look(ref storyEventIntervalHours, "storyEventIntervalHours", 12, false);
            Scribe_Values.Look(ref sceneBudgetChars, "sceneBudgetChars", 900, false);
            Scribe_Values.Look(ref loreBudgetChars, "loreBudgetChars", 500, false);
            Scribe_Values.Look(ref chapterTitle, "chapterTitle", "", false);
            Scribe_Values.Look(ref chapterNote, "chapterNote", "", false);
            Scribe_Values.Look(ref memoryEnabled, "memoryEnabled", true, false);
            Scribe_Values.Look(ref memMaxLines, "memMaxLines", 3, false);
            Scribe_Values.Look(ref memBudgetChars, "memBudgetChars", 400, false);
            Scribe_Values.Look(ref overlayEnabled, "overlayEnabled", true, false);
            Scribe_Values.Look(ref overlayMaxLines, "overlayMaxLines", 8, false);
            Scribe_Values.Look(ref overlayFontSize, "overlayFontSize", 14f, false);
            Scribe_Values.Look(ref overlayX, "overlayX", 14f, false);
            Scribe_Values.Look(ref overlayY, "overlayY", 14f, false);
            Scribe_Values.Look(ref overlayW, "overlayW", 580f, false);
            Scribe_Values.Look(ref overlayH, "overlayH", 240f, false);
        }
    }
}
