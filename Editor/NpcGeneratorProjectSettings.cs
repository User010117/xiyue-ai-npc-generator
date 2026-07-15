using System;
using System.Linq;
using UnityEditor;

namespace Xiyue.AINpcGenerator.Editor
{
    [FilePath("ProjectSettings/XiyueAINpcGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class NpcGeneratorProjectSettings : ScriptableSingleton<NpcGeneratorProjectSettings>
    {
        /// <summary>新任务使用的 Nano Banana 官方 API 模型 ID。</summary>
        public string model = SpriteSheetGenerationDefaults.ImageModel;
        public int maxConcurrency = 4;
        public string outputRoot = "Assets/XiyueGenerated/NPCs";
        public string rigProfileGuid = string.Empty;
        /// <summary>新任务是否在图片验证通过后直接转换为 NPC。</summary>
        public bool autoCompleteNpc;
        /// <summary>默认绿幕容差；命名预设可覆盖此值。</summary>
        public float greenScreenTolerance = SpriteSheetGenerationDefaults.GreenScreenTolerance;
        /// <summary>这里只保存 Unity 资产 GUID，不保存图片字节或绝对路径，确保设置文件轻量且可随资产移动恢复。</summary>
        public string[] referenceImageGuids = Array.Empty<string>();
        public bool autoAddAsNpc;

        /// <summary>
        /// 持久化项目级设置，并在写盘前收敛所有可由用户输入的边界值，避免损坏配置扩散到队列。
        /// </summary>
        public void Persist()
        {
            maxConcurrency = UnityEngine.Mathf.Clamp(maxConcurrency, 1, 20);
            greenScreenTolerance = UnityEngine.Mathf.Clamp(greenScreenTolerance, 0.05f, 0.8f);
            referenceImageGuids = (referenceImageGuids ?? Array.Empty<string>())
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Distinct(StringComparer.Ordinal)
                .Take(GeminiReferenceImageLoader.MaxImageCount)
                .ToArray();
            if (string.IsNullOrWhiteSpace(outputRoot) || !outputRoot.StartsWith("Assets/") ||
                outputRoot.Replace('\\', '/').Split('/').Contains(".."))
            {
                outputRoot = "Assets/XiyueGenerated/NPCs";
            }

            Save(true);
        }
    }

    public static class NpcGeneratorSessionSecrets
    {
        private static string apiKey;

        public static string ApiKey
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    return apiKey;
                }

                return System.Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
            }
            set => apiKey = value ?? string.Empty;
        }

        public static void Clear() => apiKey = string.Empty;
    }
}
