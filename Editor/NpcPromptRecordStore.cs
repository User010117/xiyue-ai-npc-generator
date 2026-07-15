using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Xiyue.AINpcGenerator.Editor
{
    /// <summary>
    /// 一条可恢复的角色输入记录。参考图只保存 Unity 资产 GUID；命名预设由独立资产存储复制图片。
    /// </summary>
    [Serializable]
    internal sealed class NpcPromptRecord
    {
        /// <summary>稳定记录 ID，用于区分同名之外的历史项和预设项。</summary>
        public string id;
        /// <summary>用户为预设填写的名称；历史与草稿允许为空。</summary>
        public string name;
        /// <summary>创建角色时使用的完整自然语言描述。</summary>
        public string prompt;
        /// <summary>类似 Gem 的 Sprite Sheet 生成指令；与角色描述分开编辑和复用。</summary>
        public string instruction;
        /// <summary>绿幕转透明容差，随预设绑定以保证相同素材可重复转换。</summary>
        public float greenScreenTolerance = SpriteSheetGenerationDefaults.GreenScreenTolerance;
        /// <summary>记录最后保存时间，使用 ISO 8601 UTC 字符串便于跨时区查看。</summary>
        public string savedUtc;
        /// <summary>与描述配套的参考图 Asset GUID，移动资产后仍可由 Unity 恢复。</summary>
        public string[] referenceImageGuids = Array.Empty<string>();
    }

    /// <summary>
    /// 项目本地角色输入库。草稿负责关窗恢复，历史负责追溯真实生成输入，预设由用户命名维护。
    /// </summary>
    [Serializable]
    internal sealed class NpcPromptRecordDocument
    {
        /// <summary>JSON 文档版本，未来字段变化时用于迁移。</summary>
        public string version = "2.0";
        /// <summary>窗口关闭前最后一次编辑的输入；首次使用时为空。</summary>
        public NpcPromptRecord draft;
        /// <summary>按最近使用时间倒序保存的生成历史。</summary>
        public NpcPromptRecord[] history = Array.Empty<NpcPromptRecord>();
        /// <summary>用户主动保存、可按名称覆盖的角色预设。</summary>
        public NpcPromptRecord[] presets = Array.Empty<NpcPromptRecord>();
    }

    /// <summary>
    /// 管理项目 Library 下的角色输入 JSON。使用临时文件和备份，避免 Unity 或系统中断时丢失全部记录。
    /// </summary>
    internal static class NpcPromptRecordStore
    {
        /// <summary>历史只保留最近 30 种输入，避免项目使用数月后下拉框无限增长。</summary>
        internal const int MaxHistoryCount = 30;

        /// <summary>记录目录与队列使用同一项目本地数据区，但保持独立文件和恢复边界。</summary>
        private static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/XiyueAiNpcGenerator"));
        /// <summary>用户可直接查看和备份的正式 JSON 文件。</summary>
        private static readonly string FilePath = Path.Combine(DirectoryPath, "PromptLibrary.json");
        /// <summary>原子替换前的完整临时文件。</summary>
        private static readonly string TemporaryPath = FilePath + ".tmp";
        /// <summary>上一次成功保存的备份文件。</summary>
        private static readonly string BackupPath = FilePath + ".bak";

        /// <summary>
        /// 依次从正式、备份、临时文件恢复输入库；全部不存在时返回空文档。
        /// </summary>
        public static NpcPromptRecordDocument Load(out string warning)
        {
            warning = string.Empty;
            foreach (string candidate in new[] { FilePath, BackupPath, TemporaryPath })
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    NpcPromptRecordDocument document = JsonUtility.FromJson<NpcPromptRecordDocument>(File.ReadAllText(candidate));
                    NormalizeDocument(document);
                    if (!string.Equals(candidate, FilePath, StringComparison.Ordinal))
                    {
                        warning = "角色输入记录已从备份恢复。";
                    }
                    return document;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"无法读取角色输入记录 '{Path.GetFileName(candidate)}'：{exception.Message}");
                }
            }

            return new NpcPromptRecordDocument();
        }

        /// <summary>
        /// 先写完整临时文件，再替换正式文件；调用者只在成功后向用户确认保存完成。
        /// </summary>
        public static bool TrySave(NpcPromptRecordDocument document, out string error)
        {
            try
            {
                NormalizeDocument(document);
                Directory.CreateDirectory(DirectoryPath);
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
                error = "无法保存角色输入记录：" + exception.Message;
                Debug.LogError(error);
                return false;
            }
        }

        /// <summary>更新最后编辑草稿；即使描述为空也保存，以正确恢复用户主动清空后的状态。</summary>
        public static void SetDraft(
            NpcPromptRecordDocument document,
            string prompt,
            IReadOnlyList<string> referenceImageGuids,
            string instruction = null,
            float greenScreenTolerance = SpriteSheetGenerationDefaults.GreenScreenTolerance)
        {
            document.draft = CreateRecord("draft", "当前草稿", prompt, instruction, greenScreenTolerance, referenceImageGuids, DateTime.UtcNow.ToString("O"));
        }

        /// <summary>
        /// 把一次真实入队输入放到历史顶部；相同描述和参考图只保留最近一次，批量任务不会产生重复项。
        /// </summary>
        public static NpcPromptRecord RecordHistory(
            NpcPromptRecordDocument document,
            string prompt,
            IReadOnlyList<string> referenceImageGuids,
            string savedUtc = null,
            string instruction = null,
            float greenScreenTolerance = SpriteSheetGenerationDefaults.GreenScreenTolerance)
        {
            string normalizedPrompt = prompt?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedPrompt))
            {
                return null;
            }

            string[] normalizedGuids = NormalizeGuids(referenceImageGuids);
            var history = new List<NpcPromptRecord>(document.history ?? Array.Empty<NpcPromptRecord>());
            string normalizedInstruction = NormalizeInstruction(instruction);
            float normalizedTolerance = NormalizeTolerance(greenScreenTolerance);
            NpcPromptRecord existing = history.FirstOrDefault(item => HasSameInput(item, normalizedPrompt, normalizedInstruction, normalizedTolerance, normalizedGuids));
            if (existing != null)
            {
                history.Remove(existing);
            }

            NpcPromptRecord record = CreateRecord(
                existing?.id ?? Guid.NewGuid().ToString("N"),
                string.Empty,
                normalizedPrompt,
                normalizedInstruction,
                normalizedTolerance,
                normalizedGuids,
                NormalizeTime(savedUtc));
            history.Insert(0, record);
            document.history = history.Take(MaxHistoryCount).ToArray();
            return record;
        }

        /// <summary>
        /// 按名称新增或覆盖预设；覆盖时保留稳定 ID，避免下拉框选择在更新后失效。
        /// </summary>
        public static NpcPromptRecord UpsertPreset(
            NpcPromptRecordDocument document,
            string name,
            string prompt,
            IReadOnlyList<string> referenceImageGuids,
            string instruction = null,
            float greenScreenTolerance = SpriteSheetGenerationDefaults.GreenScreenTolerance,
            string recordId = null)
        {
            string normalizedName = name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new ArgumentException("预设名称不能为空。", nameof(name));
            }

            var presets = new List<NpcPromptRecord>(document.presets ?? Array.Empty<NpcPromptRecord>());
            NpcPromptRecord record = presets.FirstOrDefault(item => string.Equals(item.name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                record = new NpcPromptRecord { id = string.IsNullOrWhiteSpace(recordId) ? Guid.NewGuid().ToString("N") : recordId };
                presets.Add(record);
            }

            record.name = normalizedName;
            record.prompt = prompt?.Trim() ?? string.Empty;
            record.instruction = NormalizeInstruction(instruction);
            record.greenScreenTolerance = NormalizeTolerance(greenScreenTolerance);
            record.savedUtc = DateTime.UtcNow.ToString("O");
            record.referenceImageGuids = NormalizeGuids(referenceImageGuids);
            document.presets = presets.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase).ToArray();
            return record;
        }

        /// <summary>仅允许按 ID 删除命名预设，生成历史不会被该操作误删。</summary>
        public static bool DeletePreset(NpcPromptRecordDocument document, string recordId)
        {
            NpcPromptRecord[] current = document.presets ?? Array.Empty<NpcPromptRecord>();
            NpcPromptRecord[] remaining = current.Where(item => item != null && item.id != recordId).ToArray();
            document.presets = remaining;
            return remaining.Length != current.Length;
        }

        /// <summary>创建并规范化一条记录，所有入口共享相同的 GUID 去重与数量边界。</summary>
        private static NpcPromptRecord CreateRecord(
            string id,
            string name,
            string prompt,
            string instruction,
            float greenScreenTolerance,
            IReadOnlyList<string> referenceImageGuids,
            string savedUtc)
        {
            return new NpcPromptRecord
            {
                id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                name = name?.Trim() ?? string.Empty,
                prompt = prompt?.Trim() ?? string.Empty,
                instruction = NormalizeInstruction(instruction),
                greenScreenTolerance = NormalizeTolerance(greenScreenTolerance),
                savedUtc = NormalizeTime(savedUtc),
                referenceImageGuids = NormalizeGuids(referenceImageGuids)
            };
        }

        /// <summary>补齐旧文档空字段并丢弃无法使用的空项，保证窗口无需反复做空值判断。</summary>
        private static void NormalizeDocument(NpcPromptRecordDocument document)
        {
            if (document == null)
            {
                throw new InvalidDataException("角色输入记录不是有效的 JSON 文档。");
            }

            document.version = "2.0";
            if (document.draft != null)
            {
                document.draft = CreateRecord("draft", "当前草稿", document.draft.prompt, document.draft.instruction, document.draft.greenScreenTolerance, document.draft.referenceImageGuids, document.draft.savedUtc);
            }
            document.history = (document.history ?? Array.Empty<NpcPromptRecord>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.prompt))
                .Select(item => CreateRecord(item.id, string.Empty, item.prompt, item.instruction, item.greenScreenTolerance, item.referenceImageGuids, item.savedUtc))
                .Take(MaxHistoryCount)
                .ToArray();
            document.presets = (document.presets ?? Array.Empty<NpcPromptRecord>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.name))
                .Select(item => CreateRecord(item.id, item.name, item.prompt, item.instruction, item.greenScreenTolerance, item.referenceImageGuids, item.savedUtc))
                .OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>过滤空 GUID、去重并限制到 Gemini 参考图上限。</summary>
        private static string[] NormalizeGuids(IReadOnlyList<string> referenceImageGuids)
        {
            return (referenceImageGuids ?? Array.Empty<string>())
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Select(guid => guid.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(GeminiReferenceImageLoader.MaxImageCount)
                .ToArray();
        }

        /// <summary>比较描述与有序参考图快照，用于历史去重。</summary>
        private static bool HasSameInput(
            NpcPromptRecord record,
            string prompt,
            string instruction,
            float greenScreenTolerance,
            IReadOnlyList<string> referenceImageGuids)
        {
            return record != null &&
                   string.Equals(record.prompt?.Trim(), prompt, StringComparison.Ordinal) &&
                   string.Equals(NormalizeInstruction(record.instruction), instruction, StringComparison.Ordinal) &&
                   Mathf.Approximately(NormalizeTolerance(record.greenScreenTolerance), greenScreenTolerance) &&
                   (record.referenceImageGuids ?? Array.Empty<string>()).SequenceEqual(referenceImageGuids ?? Array.Empty<string>());
        }

        /// <summary>旧文档缺少指令时自动迁移为默认指令。</summary>
        private static string NormalizeInstruction(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? SpriteSheetGenerationDefaults.Instruction : value.Trim();
        }

        /// <summary>容差限制在可理解范围内；旧 JSON 的默认 0 会迁移为产品默认值。</summary>
        private static float NormalizeTolerance(float value)
        {
            return value <= 0f
                ? SpriteSheetGenerationDefaults.GreenScreenTolerance
                : Mathf.Clamp(value, 0.05f, 0.8f);
        }

        /// <summary>保留可解析的原始任务时间；损坏或缺失时改用当前 UTC 时间。</summary>
        private static string NormalizeTime(string value)
        {
            return DateTime.TryParse(value, out DateTime parsed)
                ? parsed.ToUniversalTime().ToString("O")
                : DateTime.UtcNow.ToString("O");
        }
    }
}
