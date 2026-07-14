using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Xiyue.AINpcGenerator.Editor
{
    public interface INpcAiProvider
    {
        NpcAiRequestHandle BeginGenerate(string prompt, string model, string apiKey);
    }

    public abstract class NpcAiRequestHandle : IDisposable
    {
        public abstract bool IsDone { get; }
        public abstract float Progress { get; }
        public abstract void Abort();
        public abstract bool TryGetResult(out NpcCharacterSpec spec, out long statusCode, out string error);
        public abstract void Dispose();
    }

    public sealed class GeminiNpcProvider : INpcAiProvider
    {
        private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        public NpcAiRequestHandle BeginGenerate(string prompt, string model, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("Gemini API Key is empty.", nameof(apiKey));
            }

            string safeModel = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model.Trim();
            string url = string.Format(Endpoint, UnityWebRequest.EscapeURL(safeModel));
            string requestPrompt = BuildPrompt(prompt);
            var body = GeminiRequest.Create(requestPrompt);
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(json),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 120
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-goog-api-key", apiKey);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            return new GeminiRequestHandle(request, operation);
        }

        private static string BuildPrompt(string userPrompt)
        {
            return
                "Create exactly one game NPC from the user's description. " +
                "Return only the structured JSON requested by the schema. " +
                "Use concise visual tags, never file paths, Unity asset names, prefab names, or part IDs. " +
                "Use 2 to 4 personality traits and 1 to 3 short dialogue lines. " +
                "Age must be between 16 and 90. " +
                "Allowed defaultEmotion values: neutral, happy, sad, angry, surprised, afraid, curious. " +
                "Allowed movementStyle values: slow, normal, fast. " +
                "Allowed behaviorTendency values: cautious, balanced, bold, social, solitary.\n\n" +
                "User description:\n" + (userPrompt ?? string.Empty).Trim();
        }
    }

    internal sealed class GeminiRequestHandle : NpcAiRequestHandle
    {
        private UnityWebRequest request;
        private UnityWebRequestAsyncOperation operation;
        private bool consumed;

        public GeminiRequestHandle(UnityWebRequest request, UnityWebRequestAsyncOperation operation)
        {
            this.request = request;
            this.operation = operation;
        }

        public override bool IsDone => operation == null || operation.isDone;
        public override float Progress => operation?.progress ?? 1f;

        public override void Abort()
        {
            if (request != null && !IsDone)
            {
                request.Abort();
            }
        }

        public override bool TryGetResult(out NpcCharacterSpec spec, out long statusCode, out string error)
        {
            spec = null;
            statusCode = request?.responseCode ?? 0;
            error = string.Empty;

            if (!IsDone || consumed)
            {
                return false;
            }

            consumed = true;
            if (request == null)
            {
                error = "Gemini request was disposed before its result was read.";
                return true;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                error = ExtractError(request.downloadHandler?.text, request.error);
                return true;
            }

            try
            {
                var response = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                string text = response?.candidates != null && response.candidates.Length > 0 &&
                              response.candidates[0].content?.parts != null && response.candidates[0].content.parts.Length > 0
                    ? response.candidates[0].content.parts[0].text
                    : string.Empty;

                text = StripCodeFence(text);
                var envelope = JsonUtility.FromJson<NpcSpecEnvelope>(text);
                spec = envelope?.character;
                if (spec == null)
                {
                    error = "Gemini response did not contain a character object.";
                }
            }
            catch (Exception exception)
            {
                error = "Failed to parse Gemini structured output: " + exception.Message;
            }

            return true;
        }

        public override void Dispose()
        {
            operation = null;
            request?.Dispose();
            request = null;
        }

        private static string ExtractError(string json, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    GeminiErrorEnvelope envelope = JsonUtility.FromJson<GeminiErrorEnvelope>(json);
                    if (!string.IsNullOrWhiteSpace(envelope?.error?.message))
                    {
                        return envelope.error.message;
                    }
                }
                catch
                {
                    // Return the transport error below.
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? "Unknown Gemini request error." : fallback;
        }

        private static string StripCodeFence(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (!text.StartsWith("```", StringComparison.Ordinal))
            {
                return text;
            }

            int firstLine = text.IndexOf('\n');
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            return firstLine >= 0 && lastFence > firstLine
                ? text.Substring(firstLine + 1, lastFence - firstLine - 1).Trim()
                : text;
        }
    }

    [Serializable]
    internal sealed class GeminiRequest
    {
        public GeminiContent[] contents;
        public GeminiGenerationConfig generationConfig;

        public static GeminiRequest Create(string prompt)
        {
            return new GeminiRequest
            {
                contents = new[] { new GeminiContent { role = "user", parts = new[] { new GeminiPart { text = prompt } } } },
                generationConfig = new GeminiGenerationConfig
                {
                    temperature = 0.9f,
                    responseMimeType = "application/json",
                    responseSchema = GeminiRootSchema.Create()
                }
            };
        }
    }

    [Serializable] internal sealed class GeminiContent { public string role; public GeminiPart[] parts; }
    [Serializable] internal sealed class GeminiPart { public string text; }

    [Serializable]
    internal sealed class GeminiGenerationConfig
    {
        public float temperature;
        public string responseMimeType;
        public GeminiRootSchema responseSchema;
    }

    [Serializable]
    internal sealed class GeminiRootSchema
    {
        public string type = "object";
        public GeminiRootProperties properties;
        public string[] required = { "character" };

        public static GeminiRootSchema Create() => new() { properties = new GeminiRootProperties { character = GeminiCharacterSchema.Create() } };
    }

    [Serializable] internal sealed class GeminiRootProperties { public GeminiCharacterSchema character; }

    [Serializable]
    internal sealed class GeminiCharacterSchema
    {
        public string type = "object";
        public GeminiCharacterProperties properties;
        public string[] required;

        public static GeminiCharacterSchema Create()
        {
            return new GeminiCharacterSchema
            {
                properties = new GeminiCharacterProperties
                {
                    schemaVersion = GeminiStringSchema.Value(),
                    displayName = GeminiStringSchema.Value(),
                    age = new GeminiIntegerSchema { minimum = 16, maximum = 90 },
                    gender = GeminiStringSchema.Value(),
                    occupation = GeminiStringSchema.Value(),
                    faction = GeminiStringSchema.Value(),
                    personalityTraits = GeminiStringArraySchema.Value(2, 4),
                    biography = GeminiStringSchema.Value(),
                    appearance = GeminiAppearanceSchema.Create(),
                    dialogueLines = GeminiStringArraySchema.Value(1, 3),
                    defaultEmotion = GeminiStringSchema.Value(),
                    movementStyle = GeminiStringSchema.Value(),
                    behaviorTendency = GeminiStringSchema.Value()
                },
                required = new[]
                {
                    "schemaVersion", "displayName", "age", "gender", "occupation", "faction",
                    "personalityTraits", "biography", "appearance", "dialogueLines", "defaultEmotion",
                    "movementStyle", "behaviorTendency"
                }
            };
        }
    }

    [Serializable]
    internal sealed class GeminiCharacterProperties
    {
        public GeminiStringSchema schemaVersion;
        public GeminiStringSchema displayName;
        public GeminiIntegerSchema age;
        public GeminiStringSchema gender;
        public GeminiStringSchema occupation;
        public GeminiStringSchema faction;
        public GeminiStringArraySchema personalityTraits;
        public GeminiStringSchema biography;
        public GeminiAppearanceSchema appearance;
        public GeminiStringArraySchema dialogueLines;
        public GeminiStringSchema defaultEmotion;
        public GeminiStringSchema movementStyle;
        public GeminiStringSchema behaviorTendency;
    }

    [Serializable]
    internal sealed class GeminiAppearanceSchema
    {
        public string type = "object";
        public GeminiAppearanceProperties properties;
        public string[] required;

        public static GeminiAppearanceSchema Create()
        {
            return new GeminiAppearanceSchema
            {
                properties = new GeminiAppearanceProperties
                {
                    bodyType = GeminiStringSchema.Value(),
                    skinTone = GeminiStringSchema.Value(),
                    hairStyle = GeminiStringSchema.Value(),
                    hairColor = GeminiStringSchema.Value(),
                    outfitStyle = GeminiStringSchema.Value(),
                    primaryColor = GeminiStringSchema.Value(),
                    secondaryColor = GeminiStringSchema.Value(),
                    accessories = GeminiStringArraySchema.Value(0, 4),
                    weaponType = GeminiStringSchema.Value()
                },
                required = new[]
                {
                    "bodyType", "skinTone", "hairStyle", "hairColor", "outfitStyle", "primaryColor",
                    "secondaryColor", "accessories", "weaponType"
                }
            };
        }
    }

    [Serializable]
    internal sealed class GeminiAppearanceProperties
    {
        public GeminiStringSchema bodyType;
        public GeminiStringSchema skinTone;
        public GeminiStringSchema hairStyle;
        public GeminiStringSchema hairColor;
        public GeminiStringSchema outfitStyle;
        public GeminiStringSchema primaryColor;
        public GeminiStringSchema secondaryColor;
        public GeminiStringArraySchema accessories;
        public GeminiStringSchema weaponType;
    }

    [Serializable] internal sealed class GeminiStringSchema { public string type = "string"; public static GeminiStringSchema Value() => new(); }
    [Serializable] internal sealed class GeminiIntegerSchema { public string type = "integer"; public int minimum; public int maximum; }

    [Serializable]
    internal sealed class GeminiStringArraySchema
    {
        public string type = "array";
        public GeminiStringSchema items = GeminiStringSchema.Value();
        public int minItems;
        public int maxItems;
        public static GeminiStringArraySchema Value(int min, int max) => new() { minItems = min, maxItems = max };
    }

    [Serializable] internal sealed class GeminiResponse { public GeminiCandidate[] candidates; }
    [Serializable] internal sealed class GeminiCandidate { public GeminiContent content; }
    [Serializable] internal sealed class GeminiErrorEnvelope { public GeminiError error; }
    [Serializable] internal sealed class GeminiError { public int code; public string message; public string status; }
}
