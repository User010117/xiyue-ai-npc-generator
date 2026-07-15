using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Xiyue.AINpcGenerator.Editor
{
    internal static class NpcGenerationQueueStore
    {
        private static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/XiyueAiNpcGenerator"));
        private static readonly string FilePath = Path.Combine(DirectoryPath, "QueueState.json");
        private static readonly string TemporaryPath = FilePath + ".tmp";
        private static readonly string BackupPath = FilePath + ".bak";

        /// <summary>
        /// 从正式文件、备份、临时文件依次恢复队列；主文件损坏时仍尽量保住上一份完整状态。
        /// </summary>
        public static NpcGenerationJob[] Load()
        {
            foreach (string candidate in new[] { FilePath, BackupPath, TemporaryPath })
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    var document = JsonUtility.FromJson<NpcGenerationQueueDocument>(File.ReadAllText(candidate));
                    NpcGenerationJob[] jobs = document?.jobs?.Where(job => job != null).ToArray();
                    if (jobs == null)
                    {
                        throw new InvalidDataException("Queue document does not contain a jobs array.");
                    }

                    bool isVersionTwoDocument = string.Equals(document.documentVersion, "2.0", StringComparison.Ordinal);
                    foreach (NpcGenerationJob job in jobs)
                    {
                        // 旧队列没有参考图字段；恢复时补空数组，保持 1.0 文档向后兼容。
                        job.referenceImageGuids ??= Array.Empty<string>();
                        job.jobId = string.IsNullOrWhiteSpace(job.jobId) ? Guid.NewGuid().ToString("N") : job.jobId;
                        job.batchId = string.IsNullOrWhiteSpace(job.batchId) ? job.jobId : job.batchId;
                        // 缺少 workflowVersion 代表 1.x 部件任务；保留已完成记录，未完成项由队列标记为需要重新创建。
                        job.workflowVersion = string.IsNullOrWhiteSpace(job.workflowVersion)
                            ? (isVersionTwoDocument ? "2.0" : "1.0")
                            : job.workflowVersion;
                        job.instruction = string.IsNullOrWhiteSpace(job.instruction)
                            ? SpriteSheetGenerationDefaults.Instruction
                            : job.instruction;
                        if (job.greenScreenTolerance <= 0f)
                        {
                            job.greenScreenTolerance = SpriteSheetGenerationDefaults.GreenScreenTolerance;
                        }
                    }
                    if (!string.Equals(candidate, FilePath, StringComparison.Ordinal))
                    {
                        Debug.LogWarning("Recovered Xiyue AI NPC queue from " + Path.GetFileName(candidate) + ".");
                    }
                    return jobs;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Could not load Xiyue AI NPC queue candidate '{Path.GetFileName(candidate)}': {exception.Message}");
                }
            }

            return Array.Empty<NpcGenerationJob>();
        }

        /// <summary>
        /// 先完整写入临时文件，再原子替换正式文件；失败时保留临时/备份供下次启动恢复。
        /// </summary>
        public static bool TrySave(IReadOnlyList<NpcGenerationJob> jobs, out string error)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var document = new NpcGenerationQueueDocument { jobs = jobs.ToArray() };
                File.WriteAllText(TemporaryPath, JsonUtility.ToJson(document, true));
                if (File.Exists(FilePath))
                {
                    if (File.Exists(BackupPath))
                    {
                        File.Delete(BackupPath);
                    }
                    File.Replace(TemporaryPath, FilePath, BackupPath, true);
                }
                else
                {
                    File.Move(TemporaryPath, FilePath);
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not save Xiyue AI NPC queue: " + exception.Message;
                Debug.LogError(error);
                return false;
            }
        }
    }
}
