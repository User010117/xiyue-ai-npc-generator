using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Xiyue.AINpcGenerator.Editor
{
    /// <summary>
    /// 管理预设与任务的参考图副本。只复制 Unity 资产，不保存绝对路径，避免原素材移动或预设删除破坏任务。
    /// </summary>
    internal static class NpcReferenceAssetStore
    {
        /// <summary>所有命名预设的托管根目录。</summary>
        internal const string PresetRoot = "Assets/XiyueGenerated/AINpcPresets";
        /// <summary>排队任务冻结输入的根目录。</summary>
        internal const string JobRoot = "Assets/XiyueGenerated/AINpcJobs";

        /// <summary>
        /// 把当前参考图事务式替换为预设托管副本；成功后返回按原顺序排列的新 GUID。
        /// </summary>
        public static string[] ReplacePresetReferences(string presetId, string presetName, IReadOnlyList<string> sourceGuids)
        {
            if (string.IsNullOrWhiteSpace(presetId)) throw new ArgumentException("预设 ID 不能为空。", nameof(presetId));
            string shortId = presetId.Substring(0, Math.Min(8, presetId.Length));
            string finalFolder = $"{PresetRoot}/{DefaultNpcAssetWriter.SanitizeName(presetName)}_{shortId}";
            string stagingFolder = finalFolder + "_staging_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string backupFolder = finalFolder + "_backup";
            string previousFolder = FindPresetFolder(shortId);

            try
            {
                DefaultNpcAssetWriter.EnsureAssetFolder(stagingFolder);
                string[] stagedGuids = CopyReferences(stagingFolder + "/References", sourceGuids);
                if (!string.IsNullOrWhiteSpace(previousFolder))
                {
                    if (AssetDatabase.IsValidFolder(backupFolder)) AssetDatabase.DeleteAsset(backupFolder);
                    string backupError = AssetDatabase.MoveAsset(previousFolder, backupFolder);
                    if (!string.IsNullOrWhiteSpace(backupError)) throw new IOException("无法备份旧预设参考图：" + backupError);
                }

                string moveError = AssetDatabase.MoveAsset(stagingFolder, finalFolder);
                if (!string.IsNullOrWhiteSpace(moveError))
                {
                    if (AssetDatabase.IsValidFolder(backupFolder)) AssetDatabase.MoveAsset(backupFolder, previousFolder ?? finalFolder);
                    throw new IOException("无法提交预设参考图：" + moveError);
                }
                if (AssetDatabase.IsValidFolder(backupFolder)) AssetDatabase.DeleteAsset(backupFolder);
                AssetDatabase.Refresh();
                return stagedGuids.Select(guid => RemapMovedGuid(guid)).ToArray();
            }
            catch
            {
                if (AssetDatabase.IsValidFolder(stagingFolder)) AssetDatabase.DeleteAsset(stagingFolder);
                throw;
            }
        }

        /// <summary>为整个批次复制一次输入图片，批量任务共享同一份不可变快照。</summary>
        public static string[] SnapshotBatchReferences(string batchId, IReadOnlyList<string> sourceGuids)
        {
            if (string.IsNullOrWhiteSpace(batchId)) throw new ArgumentException("批次 ID 不能为空。", nameof(batchId));
            string folder = $"{JobRoot}/{batchId}/InputReferences";
            if (AssetDatabase.IsValidFolder($"{JobRoot}/{batchId}")) AssetDatabase.DeleteAsset($"{JobRoot}/{batchId}");
            return CopyReferences(folder, sourceGuids);
        }

        /// <summary>删除预设对应的托管目录；空目录或旧版无托管目录视为成功。</summary>
        public static bool DeletePresetReferences(string presetId, out string error)
        {
            string shortId = string.IsNullOrWhiteSpace(presetId) ? string.Empty : presetId.Substring(0, Math.Min(8, presetId.Length));
            string folder = FindPresetFolder(shortId);
            if (string.IsNullOrWhiteSpace(folder)) { error = string.Empty; return true; }
            bool deleted = AssetDatabase.DeleteAsset(folder);
            error = deleted ? string.Empty : "Unity 无法删除预设参考图目录：" + folder;
            return deleted;
        }

        /// <summary>复制并验证全部源资产；任一失败时删除整个目标根，避免半套参考图。</summary>
        private static string[] CopyReferences(string targetFolder, IReadOnlyList<string> sourceGuids)
        {
            string[] normalized = (sourceGuids ?? Array.Empty<string>())
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Distinct(StringComparer.Ordinal)
                .Take(GeminiReferenceImageLoader.MaxImageCount)
                .ToArray();
            GeminiReferenceImageLoader.Validate(normalized);
            if (normalized.Length == 0) return Array.Empty<string>();
            DefaultNpcAssetWriter.EnsureAssetFolder(targetFolder);
            var copied = new List<string>(normalized.Length);
            try
            {
                for (int index = 0; index < normalized.Length; index++)
                {
                    string sourcePath = AssetDatabase.GUIDToAssetPath(normalized[index]);
                    string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                    string fileName = $"{index:00}_{DefaultNpcAssetWriter.SanitizeName(Path.GetFileNameWithoutExtension(sourcePath))}{extension}";
                    string targetPath = targetFolder + "/" + fileName;
                    if (!AssetDatabase.CopyAsset(sourcePath, targetPath)) throw new IOException("无法复制参考图：" + sourcePath);
                    AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport);
                    copied.Add(AssetDatabase.AssetPathToGUID(targetPath));
                }
                return copied.ToArray();
            }
            catch
            {
                string root = targetFolder.EndsWith("/References", StringComparison.Ordinal)
                    ? targetFolder.Substring(0, targetFolder.Length - "/References".Length)
                    : targetFolder;
                if (AssetDatabase.IsValidFolder(root)) AssetDatabase.DeleteAsset(root);
                throw;
            }
        }

        /// <summary>按稳定短 ID 查找旧目录，使预设改名后仍能替换同一份托管资源。</summary>
        private static string FindPresetFolder(string shortId)
        {
            if (string.IsNullOrWhiteSpace(shortId) || !AssetDatabase.IsValidFolder(PresetRoot)) return string.Empty;
            return AssetDatabase.GetSubFolders(PresetRoot)
                .FirstOrDefault(folder => folder.EndsWith("_" + shortId, StringComparison.Ordinal));
        }

        /// <summary>AssetDatabase 移动文件夹不会改变 GUID；重新读取路径可防止未来导入器行为变化。</summary>
        private static string RemapMovedGuid(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(path) ? guid : AssetDatabase.AssetPathToGUID(path);
        }
    }
}
