using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Xiyue.AINpcGenerator.Editor
{
    internal static class NpcGenerationQueueStore
    {
        private static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/XiyueAiNpcGenerator"));
        private static readonly string FilePath = Path.Combine(DirectoryPath, "QueueState.json");

        public static NpcGenerationJob[] Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return Array.Empty<NpcGenerationJob>();
                }

                var document = JsonUtility.FromJson<NpcGenerationQueueDocument>(File.ReadAllText(FilePath));
                return document?.jobs?.Where(job => job != null).ToArray() ?? Array.Empty<NpcGenerationJob>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not load Xiyue AI NPC queue: " + exception.Message);
                return Array.Empty<NpcGenerationJob>();
            }
        }

        public static void Save(System.Collections.Generic.IReadOnlyList<NpcGenerationJob> jobs)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var document = new NpcGenerationQueueDocument { jobs = jobs.ToArray() };
                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(document, true));
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }

                File.Move(temporary, FilePath);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not save Xiyue AI NPC queue: " + exception.Message);
            }
        }
    }
}
