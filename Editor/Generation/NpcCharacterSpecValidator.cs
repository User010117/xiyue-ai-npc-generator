using System;
using System.Collections.Generic;
using System.Linq;

namespace Xiyue.AINpcGenerator.Editor
{
    public sealed class NpcValidationResult
    {
        public readonly List<string> errors = new();
        public readonly List<string> warnings = new();
        public bool IsValid => errors.Count == 0;
    }

    public static class NpcCharacterSpecValidator
    {
        private static readonly HashSet<string> Emotions = new(StringComparer.OrdinalIgnoreCase)
        {
            "neutral", "happy", "sad", "angry", "surprised", "afraid", "curious"
        };

        private static readonly HashSet<string> MovementStyles = new(StringComparer.OrdinalIgnoreCase)
        {
            "slow", "normal", "fast"
        };

        private static readonly HashSet<string> BehaviorTendencies = new(StringComparer.OrdinalIgnoreCase)
        {
            "cautious", "balanced", "bold", "social", "solitary"
        };

        public static NpcValidationResult ValidateAndNormalize(NpcCharacterSpec spec)
        {
            var result = new NpcValidationResult();
            if (spec == null)
            {
                result.errors.Add("Gemini returned no character object.");
                return result;
            }

            spec.NormalizeCollections();
            spec.displayName = Clean(spec.displayName, 48, "Unnamed NPC");
            spec.occupation = Clean(spec.occupation, 48, "traveler");
            spec.faction = Clean(spec.faction, 48, "neutral");
            spec.gender = Clean(spec.gender, 24, "unspecified").ToLowerInvariant();
            spec.biography = Clean(spec.biography, 360, string.Empty);
            spec.age = Math.Clamp(spec.age, 16, 90);

            spec.personalityTraits = CleanArray(spec.personalityTraits, 2, 4, 32);
            if (spec.personalityTraits.Length < 2)
            {
                result.errors.Add("At least two personality traits are required.");
            }

            spec.dialogueLines = CleanArray(spec.dialogueLines, 1, 3, 100);
            if (spec.dialogueLines.Length == 0)
            {
                result.errors.Add("At least one dialogue line is required.");
            }

            spec.defaultEmotion = NormalizeEnum(spec.defaultEmotion, Emotions, "neutral", result);
            spec.movementStyle = NormalizeEnum(spec.movementStyle, MovementStyles, "normal", result);
            spec.behaviorTendency = NormalizeEnum(spec.behaviorTendency, BehaviorTendencies, "balanced", result);

            NpcAppearanceSpec appearance = spec.appearance;
            appearance.bodyType = CleanTag(appearance.bodyType, "average");
            appearance.skinTone = CleanTag(appearance.skinTone, "medium");
            appearance.hairStyle = CleanTag(appearance.hairStyle, "short");
            appearance.hairColor = CleanTag(appearance.hairColor, "brown");
            appearance.outfitStyle = CleanTag(appearance.outfitStyle, "casual");
            appearance.primaryColor = CleanTag(appearance.primaryColor, "blue");
            appearance.secondaryColor = CleanTag(appearance.secondaryColor, "white");
            appearance.weaponType = CleanTag(appearance.weaponType, "none");
            appearance.accessories = CleanArray(appearance.accessories, 0, 4, 32)
                .Select(x => CleanTag(x, string.Empty))
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();

            return result;
        }

        private static string NormalizeEnum(string value, HashSet<string> allowed, string fallback, NpcValidationResult result)
        {
            string normalized = CleanTag(value, fallback);
            if (allowed.Contains(normalized))
            {
                return normalized;
            }

            result.warnings.Add($"Unknown value '{normalized}' was normalized to '{fallback}'.");
            return fallback;
        }

        private static string[] CleanArray(string[] values, int minimum, int maximum, int maxLength)
        {
            if (values == null)
            {
                return Array.Empty<string>();
            }

            return values
                .Select(value => Clean(value, maxLength, string.Empty))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maximum)
                .ToArray();
        }

        private static string Clean(string value, int maxLength, string fallback)
        {
            string cleaned = (value ?? string.Empty).Trim();
            if (cleaned.Length == 0)
            {
                return fallback;
            }

            return cleaned.Length <= maxLength ? cleaned : cleaned.Substring(0, maxLength).TrimEnd();
        }

        private static string CleanTag(string value, string fallback)
        {
            string cleaned = Clean(value, 40, fallback).ToLowerInvariant();
            return cleaned.Replace(' ', '_').Replace('-', '_');
        }
    }
}
