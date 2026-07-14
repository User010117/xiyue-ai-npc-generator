using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    public enum NpcPartSlot
    {
        BackAccessory = 0,
        Body = 10,
        LowerOutfit = 20,
        UpperOutfit = 30,
        HairBack = 40,
        Head = 50,
        HairFront = 60,
        Hat = 70,
        Weapon = 80,
        FrontAccessory = 90
    }

    public enum NpcPartTintMode
    {
        None,
        Skin,
        Hair,
        PrimaryOutfit,
        SecondaryOutfit
    }

    [Serializable]
    public sealed class NpcPartEntry
    {
        public string partId = "part";
        public NpcPartSlot slot;
        public Texture2D atlas;
        public NpcPartTintMode tintMode;
        [Min(0.01f)] public float weight = 1f;
        public bool isFallback;
        public string[] tags = Array.Empty<string>();
        public string[] incompatibleTags = Array.Empty<string>();
    }

    [CreateAssetMenu(menuName = "Xiyue/AI NPC/Part Catalog", fileName = "NpcPartCatalog")]
    public sealed class NpcPartCatalog : ScriptableObject
    {
        public string catalogVersion = "1.0";
        public List<NpcPartEntry> parts = new();
    }

    [Serializable]
    public sealed class NpcResolvedPart
    {
        public NpcPartSlot slot;
        public string partId;
        public NpcPartTintMode tintMode;
    }

    [Serializable]
    public sealed class NpcResolvedAppearance
    {
        public int seed;
        public string fingerprint;
        public List<NpcResolvedPart> parts = new();
    }
}
