using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Xiyue.AINpcGenerator.Editor
{
    public sealed class NpcCatalogValidationReport
    {
        public readonly List<string> errors = new();
        public readonly List<string> warnings = new();
        public bool IsValid => errors.Count == 0;
    }

    public static class NpcPartCatalogValidator
    {
        private static readonly NpcPartSlot[] RequiredSlots =
        {
            NpcPartSlot.Body,
            NpcPartSlot.UpperOutfit
        };

        public static NpcCatalogValidationReport Validate(NpcRigProfile profile, NpcPartCatalog catalog)
        {
            var report = new NpcCatalogValidationReport();
            if (profile == null)
            {
                report.errors.Add("Rig profile is missing.");
                return report;
            }

            if (!profile.IsValid(out string profileError))
            {
                report.errors.Add(profileError);
                return report;
            }

            if (catalog == null)
            {
                report.errors.Add("Part catalog is missing.");
                return report;
            }

            if (catalog.parts == null || catalog.parts.Count == 0)
            {
                report.errors.Add("Part catalog is empty.");
                return report;
            }

            foreach (IGrouping<string, NpcPartEntry> group in catalog.parts
                         .Where(part => part != null)
                         .GroupBy(part => part.partId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    report.errors.Add("Every catalog part needs a non-empty partId.");
                }
                else if (group.Count() > 1)
                {
                    report.errors.Add($"Duplicate partId '{group.Key}'.");
                }
            }

            foreach (NpcPartSlot slot in RequiredSlots)
            {
                if (!catalog.parts.Any(part => part != null && part.slot == slot && part.isFallback))
                {
                    report.errors.Add($"Required slot '{slot}' needs one certified fallback part.");
                }
            }

            foreach (NpcPartEntry part in catalog.parts.Where(part => part != null))
            {
                ValidatePart(profile, part, report);
            }

            return report;
        }

        private static void ValidatePart(NpcRigProfile profile, NpcPartEntry part, NpcCatalogValidationReport report)
        {
            string label = string.IsNullOrWhiteSpace(part.partId) ? "<unnamed>" : part.partId;
            if (part.atlas == null)
            {
                report.errors.Add($"Part '{label}' has no atlas.");
                return;
            }

            if (part.atlas.width != profile.AtlasWidth || part.atlas.height != profile.AtlasHeight)
            {
                report.errors.Add($"Part '{label}' atlas must be {profile.AtlasWidth}x{profile.AtlasHeight}.");
                return;
            }

            Color32[] pixels;
            try
            {
                pixels = part.atlas.GetPixels32();
            }
            catch (Exception)
            {
                report.errors.Add($"Part '{label}' atlas must have Read/Write enabled.");
                return;
            }

            for (int direction = 0; direction < profile.directions; direction++)
            {
                int firstFootY = -1;
                for (int frame = 0; frame < profile.framesPerDirection; frame++)
                {
                    if (!FrameHasVisiblePixel(pixels, part.atlas.width, profile, direction, frame))
                    {
                        report.errors.Add($"Part '{label}' has an empty frame at direction {direction}, frame {frame}.");
                        return;
                    }

                    if (FrameTouchesBorder(pixels, part.atlas.width, profile, direction, frame))
                    {
                        report.errors.Add($"Part '{label}' touches a frame border at direction {direction}, frame {frame} and may be clipped.");
                        return;
                    }

                    if (part.slot == NpcPartSlot.Body)
                    {
                        int footY = LowestVisibleY(pixels, part.atlas.width, profile, direction, frame);
                        if (firstFootY < 0) firstFootY = footY;
                        else if (Math.Abs(footY - firstFootY) > 2)
                        {
                            report.errors.Add($"Body part '{label}' has an unstable foot anchor at direction {direction}, frame {frame}.");
                            return;
                        }
                    }
                }
            }

            part.tags ??= Array.Empty<string>();
            part.incompatibleTags ??= Array.Empty<string>();
            if (part.weight <= 0f)
            {
                report.errors.Add($"Part '{label}' must have a positive selection weight.");
            }
        }

        private static bool FrameHasVisiblePixel(
            Color32[] pixels,
            int atlasWidth,
            NpcRigProfile profile,
            int direction,
            int frame)
        {
            int startX = frame * profile.frameWidth;
            int startY = direction * profile.frameHeight;
            for (int y = 0; y < profile.frameHeight; y++)
            {
                int row = (startY + y) * atlasWidth;
                for (int x = 0; x < profile.frameWidth; x++)
                {
                    if (pixels[row + startX + x].a > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool FrameTouchesBorder(
            Color32[] pixels,
            int atlasWidth,
            NpcRigProfile profile,
            int direction,
            int frame)
        {
            int startX = frame * profile.frameWidth;
            int startY = direction * profile.frameHeight;
            int maxX = startX + profile.frameWidth - 1;
            int maxY = startY + profile.frameHeight - 1;
            for (int x = startX; x <= maxX; x++)
            {
                if (pixels[startY * atlasWidth + x].a > 0 || pixels[maxY * atlasWidth + x].a > 0) return true;
            }
            for (int y = startY; y <= maxY; y++)
            {
                if (pixels[y * atlasWidth + startX].a > 0 || pixels[y * atlasWidth + maxX].a > 0) return true;
            }
            return false;
        }

        private static int LowestVisibleY(
            Color32[] pixels,
            int atlasWidth,
            NpcRigProfile profile,
            int direction,
            int frame)
        {
            int startX = frame * profile.frameWidth;
            int startY = direction * profile.frameHeight;
            for (int localY = 0; localY < profile.frameHeight; localY++)
            {
                int row = (startY + localY) * atlasWidth;
                for (int localX = 0; localX < profile.frameWidth; localX++)
                {
                    if (pixels[row + startX + localX].a > 0) return localY;
                }
            }
            return -1;
        }
    }
}
