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
        NeedsReview
    }

    [Serializable]
    public sealed class NpcGenerationJob
    {
        public string jobId;
        public string batchId;
        public string createdUtc;
        public string prompt;
        public string model;
        public int seed;
        public NpcGenerationStatus status;
        public float progress;
        public string statusMessage;
        public string error;
        public int retryCount;
        public double retryAtEpochSeconds;
        public string profileGuid;
        public string catalogGuid;
        public string outputRoot;
        public bool autoAddAsNpc;
        public NpcCharacterSpec character;
        public NpcResolvedAppearance resolvedAppearance;
        public string outputAssetPath;

        public bool HasCharacterData => character != null && !string.IsNullOrWhiteSpace(character.displayName);
    }

    [Serializable]
    internal sealed class NpcGenerationQueueDocument
    {
        public string documentVersion = "1.0";
        public NpcGenerationJob[] jobs = Array.Empty<NpcGenerationJob>();
    }
}
