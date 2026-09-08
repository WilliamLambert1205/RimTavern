using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using RimTavern.Util;

namespace RimTavern.Logic
{
    /// <summary>One chat message in the LLM conversation.</summary>
    public class LlmMessage
    {
        public string role;      // system | user | assistant
        public string content;

        public LlmMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Fields["role"] = new JsonString(role);
            o.Fields["content"] = new JsonString(content);
            return o;
        }
    }

    /// <summary>
    /// Minimal OpenAI-compatible chat-completions client.
    /// Runs on background threads; never touches Unity/RimWorld objects.
    /// Adapted from the author's previous project AICharacterGen (Logic/LlmClient.cs).
    /// </summary>
    public class LlmClient
    {
        private static HttpClient shared;
        private readonly string baseUrl;
        private readonly string apiKey;
        private readonly string model;
        private readonly float temperature;

        public LlmClient(string baseUrl, string apiKey, string model, float temperature)
        {
            this.baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            this.apiKey = apiKey ?? "";
            this.model = string.IsNullOrEmpty(model) ? "deepseek-chat" : model;
            this.temperature = temperature;
            EnsureTls();
            if (shared == null)
            {
                shared = new HttpClient();
                shared.Timeout = TimeSpan.FromMinutes(3);
            }
        }

        private static void EnsureTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception) { }
        }

        public string Model { get { return model; } }

        /// <summary>Call /chat/completions. Returns assistant content or throws.</summary>
        public string Chat(List<LlmMessage> messages, int? maxTokens = null, CancellationToken ct = default(CancellationToken))
        {
            string url = baseUrl + "/chat/completions";

            var body = new JsonObject();
            body.Fields["model"] = new JsonString(model);
            body.Fields["temperature"] = new JsonNumber(temperature);
            var arr = new JsonArray();
            foreach (var m in messages) arr.Items.Add(m.ToJson());
            body.Fields["messages"] = arr;
            if (maxTokens.HasValue) body.Fields["max_tokens"] = new JsonNumber(maxTokens.Value);

            string json = body.ToJson();
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(apiKey))
                {
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                }
                using (HttpResponseMessage resp = shared.SendAsync(req, ct).GetAwaiter().GetResult())
                {
                    string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new Exception("LLM HTTP " + (int)resp.StatusCode + ": " + Truncate(text, 500));
                    }
                    JsonNode root = Json.Parse(text);
                    if (root == null) throw new Exception("LLM returned unparseable response: " + Truncate(text, 500));
                    JsonArray choices = root.GetArray("choices");
                    if (choices == null || choices.Items.Count == 0)
                        throw new Exception("LLM response has no choices: " + Truncate(text, 500));
                    JsonNode msg = choices.Items[0].Get("message");
                    string content = msg != null ? msg.GetStr("content") : null;
                    if (content == null) throw new Exception("LLM response has no message content: " + Truncate(text, 500));
                    return content;
                }
            }
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }

    /// <summary>Builds clients from current mod settings.</summary>
    public static class LlmClientFactory
    {
        public static LlmClient Create()
        {
            var s = RimTavernMod.Settings;
            return new LlmClient(s.baseUrl, s.apiKey, s.model, s.temperature);
        }

        /// <summary>Quick connectivity test. Returns reply, or null + error on failure.</summary>
        public static string QuickChat(string prompt, out string error)
        {
            error = null;
            try
            {
                var client = Create();
                var msgs = new List<LlmMessage>
                {
                    new LlmMessage("system", "You are a helpful assistant. Reply briefly."),
                    new LlmMessage("user", prompt)
                };
                return client.Chat(msgs, 64);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
    }
}
