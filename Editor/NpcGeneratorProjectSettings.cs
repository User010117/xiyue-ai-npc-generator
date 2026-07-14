using System.Linq;
using UnityEditor;

namespace Xiyue.AINpcGenerator.Editor
{
    [FilePath("ProjectSettings/XiyueAINpcGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class NpcGeneratorProjectSettings : ScriptableSingleton<NpcGeneratorProjectSettings>
    {
        public string model = "gemini-2.5-flash";
        public int maxConcurrency = 4;
        public string outputRoot = "Assets/XiyueGenerated/NPCs";
        public string rigProfileGuid = string.Empty;
        public string partCatalogGuid = string.Empty;
        public bool autoAddAsNpc;

        public void Persist()
        {
            maxConcurrency = UnityEngine.Mathf.Clamp(maxConcurrency, 1, 20);
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
