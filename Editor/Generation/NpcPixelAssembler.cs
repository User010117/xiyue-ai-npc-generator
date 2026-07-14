using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xiyue.AINpcGenerator.Editor
{
    public interface INpcAssembler
    {
        Texture2D Compose(NpcRigProfile profile, NpcCharacterSpec spec, IReadOnlyList<NpcPartEntry> parts);
    }

    public sealed class NpcPixelAssembler : INpcAssembler
    {
        public Texture2D Compose(NpcRigProfile profile, NpcCharacterSpec spec, IReadOnlyList<NpcPartEntry> parts)
        {
            if (profile == null || spec == null || parts == null)
            {
                throw new ArgumentNullException(profile == null ? nameof(profile) : spec == null ? nameof(spec) : nameof(parts));
            }

            var output = new Texture2D(profile.AtlasWidth, profile.AtlasHeight, TextureFormat.RGBA32, false)
            {
                name = "GeneratedNpcAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var destination = new Color32[profile.AtlasWidth * profile.AtlasHeight];

            foreach (NpcPartEntry part in parts)
            {
                Color32[] source = part.atlas.GetPixels32();
                Color tint = ResolveTint(part.tintMode, spec.appearance);
                for (int index = 0; index < destination.Length; index++)
                {
                    Color32 pixel = source[index];
                    if (pixel.a == 0)
                    {
                        continue;
                    }

                    var tinted = new Color(
                        (pixel.r / 255f) * tint.r,
                        (pixel.g / 255f) * tint.g,
                        (pixel.b / 255f) * tint.b,
                        pixel.a / 255f);
                    destination[index] = AlphaBlend(destination[index], tinted);
                }
            }

            output.SetPixels32(destination);
            output.Apply(false, false);
            return output;
        }

        private static Color ResolveTint(NpcPartTintMode mode, NpcAppearanceSpec appearance)
        {
            return mode switch
            {
                NpcPartTintMode.Skin => NpcColorParser.Parse(appearance.skinTone, new Color(0.72f, 0.48f, 0.32f)),
                NpcPartTintMode.Hair => NpcColorParser.Parse(appearance.hairColor, new Color(0.25f, 0.13f, 0.07f)),
                NpcPartTintMode.PrimaryOutfit => NpcColorParser.Parse(appearance.primaryColor, new Color(0.15f, 0.35f, 0.85f)),
                NpcPartTintMode.SecondaryOutfit => NpcColorParser.Parse(appearance.secondaryColor, Color.white),
                _ => Color.white
            };
        }

        private static Color32 AlphaBlend(Color32 destination, Color source)
        {
            float sourceAlpha = Mathf.Clamp01(source.a);
            float destinationAlpha = destination.a / 255f;
            float outputAlpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
            if (outputAlpha <= 0f)
            {
                return new Color32(0, 0, 0, 0);
            }

            float r = (source.r * sourceAlpha + (destination.r / 255f) * destinationAlpha * (1f - sourceAlpha)) / outputAlpha;
            float g = (source.g * sourceAlpha + (destination.g / 255f) * destinationAlpha * (1f - sourceAlpha)) / outputAlpha;
            float b = (source.b * sourceAlpha + (destination.b / 255f) * destinationAlpha * (1f - sourceAlpha)) / outputAlpha;
            return new Color(r, g, b, outputAlpha);
        }
    }

    public static class NpcColorParser
    {
        private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = new Color(0.08f, 0.08f, 0.1f),
            ["white"] = new Color(0.95f, 0.95f, 0.92f),
            ["gray"] = new Color(0.42f, 0.45f, 0.5f),
            ["grey"] = new Color(0.42f, 0.45f, 0.5f),
            ["red"] = new Color(0.75f, 0.12f, 0.12f),
            ["blue"] = new Color(0.14f, 0.34f, 0.85f),
            ["green"] = new Color(0.18f, 0.58f, 0.28f),
            ["yellow"] = new Color(0.92f, 0.72f, 0.13f),
            ["purple"] = new Color(0.55f, 0.24f, 0.72f),
            ["pink"] = new Color(0.9f, 0.48f, 0.65f),
            ["brown"] = new Color(0.32f, 0.17f, 0.08f),
            ["orange"] = new Color(0.9f, 0.4f, 0.08f),
            ["cyan"] = new Color(0.12f, 0.75f, 0.8f),
            ["gold"] = new Color(0.85f, 0.62f, 0.12f),
            ["silver"] = new Color(0.68f, 0.72f, 0.78f),
            ["pale"] = new Color(0.95f, 0.76f, 0.62f),
            ["fair"] = new Color(0.9f, 0.66f, 0.5f),
            ["medium"] = new Color(0.72f, 0.48f, 0.32f),
            ["tan"] = new Color(0.62f, 0.38f, 0.22f),
            ["dark"] = new Color(0.33f, 0.18f, 0.11f)
        };

        public static Color Parse(string value, Color fallback)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
            if (NamedColors.TryGetValue(normalized, out Color named))
            {
                return named;
            }

            if (normalized.StartsWith("#", StringComparison.Ordinal) && ColorUtility.TryParseHtmlString(normalized, out Color html))
            {
                return html;
            }

            return fallback;
        }
    }
}
