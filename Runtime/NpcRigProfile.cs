using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    [CreateAssetMenu(menuName = "Xiyue/AI NPC/Rig Profile", fileName = "NpcRigProfile")]
    public sealed class NpcRigProfile : ScriptableObject
    {
        [Min(8)] public int frameWidth = 48;
        [Min(8)] public int frameHeight = 48;
        [Min(1)] public int pixelsPerUnit = 48;
        [Range(1, 8)] public int directions = 4;
        [Range(1, 16)] public int framesPerDirection = 4;
        [Range(1f, 30f)] public float animationFrameRate = 8f;
        public Vector2 pivot = new(0.5f, 0.08f);
        public string[] directionNames = { "Down", "Left", "Right", "Up" };

        public int AtlasWidth => frameWidth * framesPerDirection;
        public int AtlasHeight => frameHeight * directions;
        public int SpriteCount => directions * framesPerDirection;

        public bool IsValid(out string error)
        {
            if (frameWidth < 8 || frameHeight < 8)
            {
                error = "Frame size must be at least 8 x 8 pixels.";
                return false;
            }

            if (directions != 4)
            {
                error = "V1 supports exactly four directions.";
                return false;
            }

            if (framesPerDirection < 1)
            {
                error = "At least one frame per direction is required.";
                return false;
            }

            if (directionNames == null || directionNames.Length != directions)
            {
                error = "Direction names must match the direction count.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
