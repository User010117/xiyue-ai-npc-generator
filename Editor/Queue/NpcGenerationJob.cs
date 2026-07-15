using System;

namespace Xiyue.AINpcGenerator.Editor
{
    public enum NpcGenerationStatus
    {
        Pending,
        GeneratingData,
        ValidatingData,
        SelectingParts,
        Assembling,
        Importing,
        QualityChecking,
        Completed,
        Paused,
        RetryWaiting,
        Failed,
        Cancelled,
        Interrupted,
        NeedsReview,
        // 新状态只能追加，不能重排；JsonUtility 会把枚举按整数保存到旧队列。
        GeneratingImage,
        SavingPreview,
        AwaitingImageReview,
        ConvertingSpriteSheet
    }

    [Serializable]
    public sealed class NpcGenerationJob
    {
        public string jobId;
        public string batchId;
        public string createdUtc;
        public string prompt;
        /// <summary>任务创建时冻结的可编辑 Sprite Sheet 指令。</summary>
        public string instruction;
        /// <summary>用于本地基础角色名称；空值时由描述生成。</summary>
        public string presetName;
        public string model;
        public int seed;
        public NpcGenerationStatus status;
        public float progress;
        public string statusMessage;
        public string error;
        public int retryCount;
        public double retryAtEpochSeconds;
        public string profileGuid;
        /// <summary>旧版部件任务字段，仅用于恢复旧完成记录；新任务不再依赖部件库。</summary>
        public string catalogGuid;
        /// <summary>每个任务冻结创建时的参考图 GUID；之后修改窗口设置不会改变已排队任务的输入。</summary>
        public string[] referenceImageGuids = Array.Empty<string>();
        public string outputRoot;
        /// <summary>验证通过后是否跳过人工确认直接生成预制体。</summary>
        public bool autoCompleteNpc;
        /// <summary>绿幕转透明容差，创建任务后不随窗口设置变化。</summary>
        public float greenScreenTolerance = SpriteSheetGenerationDefaults.GreenScreenTolerance;
        public bool autoAddAsNpc;
        public NpcCharacterSpec character;
        public NpcResolvedAppearance resolvedAppearance;
        public string outputAssetPath;
        /// <summary>Gemini 原始 Sprite Sheet PNG 资产路径，可在重载后继续预览。</summary>
        public string generatedSpriteSheetPath;
        /// <summary>图片布局或绿幕检测警告；自动模式遇到警告必须停在预览。</summary>
        public string imageValidationWarning;
        /// <summary>区分旧部件任务与新图片任务。</summary>
        // 保持空默认值，让旧 JSON 缺失该字段时可被队列存储层可靠识别为 1.x 工作流。
        public string workflowVersion = string.Empty;

        /// <summary>
        /// 返回适合界面和文件夹后缀使用的安全短 ID；损坏的旧队列也不会再因 Substring 抛异常。
        /// </summary>
        public string ShortJobId => string.IsNullOrWhiteSpace(jobId)
            ? "unknown"
            : jobId.Substring(0, Math.Min(8, jobId.Length));

        public bool HasCharacterData => character != null && !string.IsNullOrWhiteSpace(character.displayName);
    }

    [Serializable]
    internal sealed class NpcGenerationQueueDocument
    {
        /// <summary>2.0 新增图片预览、指令、绿幕和确认状态。</summary>
        public string documentVersion = "2.0";
        public NpcGenerationJob[] jobs = Array.Empty<NpcGenerationJob>();
    }
}
