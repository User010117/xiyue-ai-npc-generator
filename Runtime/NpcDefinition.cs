using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    [CreateAssetMenu(menuName = "Xiyue/AI NPC/NPC Definition", fileName = "NpcDefinition")]
    public sealed class NpcDefinition : ScriptableObject
    {
        [SerializeField] private string npcId;
        [SerializeField] private NpcCharacterSpec character = new();
        [SerializeField] private int generationSeed;
        [SerializeField] private string sourcePrompt;
        [SerializeField] private string modelName;
        [SerializeField] private string catalogVersion;
        [SerializeField] private string appearanceFingerprint;
        [SerializeField] private NpcResolvedAppearance resolvedAppearance = new();
        [SerializeField] private Sprite previewSprite;
        [SerializeField] private AnimationClip[] generatedClips;
        [SerializeField] private float moveSpeed = 1.5f;
        [SerializeField] private float interactionRadius = 1.8f;

        public string NpcId => npcId;
        public NpcCharacterSpec Character => character;
        public int GenerationSeed => generationSeed;
        public string SourcePrompt => sourcePrompt;
        public string ModelName => modelName;
        public string CatalogVersion => catalogVersion;
        public string AppearanceFingerprint => appearanceFingerprint;
        public NpcResolvedAppearance ResolvedAppearance => resolvedAppearance;
        public Sprite PreviewSprite => previewSprite;
        public AnimationClip[] GeneratedClips => generatedClips;
        public float MoveSpeed => moveSpeed;
        public float InteractionRadius => interactionRadius;

        public void Initialize(
            string id,
            NpcCharacterSpec spec,
            int seed,
            string prompt,
            string model,
            string version,
            NpcResolvedAppearance resolved,
            Sprite preview,
            AnimationClip[] clips,
            float speed)
        {
            npcId = id;
            character = spec;
            generationSeed = seed;
            sourcePrompt = prompt;
            modelName = model;
            catalogVersion = version;
            appearanceFingerprint = resolved?.fingerprint ?? string.Empty;
            resolvedAppearance = resolved ?? new NpcResolvedAppearance();
            previewSprite = preview;
            generatedClips = clips;
            moveSpeed = Mathf.Clamp(speed, 0.25f, 8f);
        }
    }
}
