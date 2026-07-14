using System;
using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    [Serializable]
    public sealed class NpcAppearanceSpec
    {
        public string bodyType = "average";
        public string skinTone = "medium";
        public string hairStyle = "short";
        public string hairColor = "brown";
        public string outfitStyle = "casual";
        public string primaryColor = "blue";
        public string secondaryColor = "white";
        public string[] accessories = Array.Empty<string>();
        public string weaponType = "none";

        public void NormalizeCollections()
        {
            accessories ??= Array.Empty<string>();
        }
    }

    [Serializable]
    public sealed class NpcCharacterSpec
    {
        public string schemaVersion = "1.0";
        public string displayName = "Unnamed NPC";
        public int age = 25;
        public string gender = "unspecified";
        public string occupation = "traveler";
        public string faction = "neutral";
        public string[] personalityTraits = Array.Empty<string>();
        [TextArea(2, 5)] public string biography = string.Empty;
        public NpcAppearanceSpec appearance = new();
        public string[] dialogueLines = Array.Empty<string>();
        public string defaultEmotion = "neutral";
        public string movementStyle = "normal";
        public string behaviorTendency = "balanced";

        public void NormalizeCollections()
        {
            personalityTraits ??= Array.Empty<string>();
            dialogueLines ??= Array.Empty<string>();
            appearance ??= new NpcAppearanceSpec();
            appearance.NormalizeCollections();
        }
    }

    [Serializable]
    public sealed class NpcSpecEnvelope
    {
        public NpcCharacterSpec character = new();
    }
}
