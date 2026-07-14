using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Xiyue.AINpcGenerator.Editor
{
    public sealed class NpcPartResolution
    {
        public readonly List<NpcPartEntry> sourceParts = new();
        public NpcResolvedAppearance resolvedAppearance = new();
    }

    public interface INpcPartResolver
    {
        NpcPartResolution Resolve(NpcCharacterSpec spec, NpcPartCatalog catalog, int seed);
    }

    public sealed class DeterministicNpcPartResolver : INpcPartResolver
    {
        private static readonly NpcPartSlot[] AlwaysConsider =
        {
            NpcPartSlot.BackAccessory,
            NpcPartSlot.Body,
            NpcPartSlot.LowerOutfit,
            NpcPartSlot.UpperOutfit,
            NpcPartSlot.HairBack,
            NpcPartSlot.Head,
            NpcPartSlot.HairFront,
            NpcPartSlot.Hat,
            NpcPartSlot.Weapon,
            NpcPartSlot.FrontAccessory
        };

        public NpcPartResolution Resolve(NpcCharacterSpec spec, NpcPartCatalog catalog, int seed)
        {
            if (spec == null || catalog == null)
            {
                throw new ArgumentNullException(spec == null ? nameof(spec) : nameof(catalog));
            }

            HashSet<string> desiredTags = BuildDesiredTags(spec);
            HashSet<string> priorityTags = BuildPriorityTags(spec);
            var result = new NpcPartResolution
            {
                resolvedAppearance = new NpcResolvedAppearance { seed = seed }
            };

            foreach (NpcPartSlot slot in AlwaysConsider)
            {
                List<NpcPartEntry> entries = catalog.parts
                    .Where(part => part != null && part.atlas != null && part.slot == slot)
                    .ToList();
                if (entries.Count == 0)
                {
                    continue;
                }

                bool optionalRequested = IsOptionalSlotRequested(slot, spec, desiredTags, entries);
                if (IsOptional(slot) && !optionalRequested)
                {
                    continue;
                }

                NpcPartEntry selected = Select(entries, desiredTags, priorityTags, seed, slot);
                if (selected == null)
                {
                    if (slot is NpcPartSlot.Body or NpcPartSlot.UpperOutfit)
                    {
                        throw new InvalidOperationException($"No compatible or fallback part exists for required slot '{slot}'.");
                    }
                    continue;
                }

                if (selected.incompatibleTags != null && selected.incompatibleTags.Any(tag => desiredTags.Contains(Normalize(tag))))
                {
                    NpcPartEntry fallback = entries.FirstOrDefault(entry => entry.isFallback);
                    if (fallback == null || fallback == selected)
                    {
                        continue;
                    }
                    selected = fallback;
                }

                result.sourceParts.Add(selected);
                result.resolvedAppearance.parts.Add(new NpcResolvedPart
                {
                    slot = selected.slot,
                    partId = selected.partId,
                    tintMode = selected.tintMode
                });
            }

            result.sourceParts.Sort((left, right) => ((int)left.slot).CompareTo((int)right.slot));
            result.resolvedAppearance.parts.Sort((left, right) => ((int)left.slot).CompareTo((int)right.slot));
            result.resolvedAppearance.fingerprint = BuildFingerprint(result.resolvedAppearance.parts, seed);
            return result;
        }

        private static NpcPartEntry Select(
            List<NpcPartEntry> entries,
            HashSet<string> desiredTags,
            HashSet<string> priorityTags,
            int seed,
            NpcPartSlot slot)
        {
            int bestScore = entries.Max(entry => Score(entry, desiredTags, priorityTags));
            List<NpcPartEntry> candidates = bestScore > 0
                ? entries.Where(entry => Score(entry, desiredTags, priorityTags) == bestScore).ToList()
                : entries.Where(entry => entry.isFallback).ToList();
            if (candidates.Count == 0)
            {
                candidates = entries;
            }

            candidates.Sort((left, right) => string.CompareOrdinal(left.partId, right.partId));
            uint state = unchecked((uint)(seed ^ StableHash(slot.ToString())));
            float totalWeight = candidates.Sum(candidate => Math.Max(0.01f, candidate.weight));
            float pick = Next01(ref state) * totalWeight;
            foreach (NpcPartEntry candidate in candidates)
            {
                pick -= Math.Max(0.01f, candidate.weight);
                if (pick <= 0f)
                {
                    return candidate;
                }
            }

            return candidates[^1];
        }

        private static int Score(NpcPartEntry entry, HashSet<string> desiredTags, HashSet<string> priorityTags)
        {
            if (entry.tags == null)
            {
                return 0;
            }

            int score = 0;
            foreach (string tag in entry.tags.Select(Normalize))
            {
                if (priorityTags.Contains(tag)) score += 10;
                else if (desiredTags.Contains(tag)) score += 1;
            }
            return score;
        }

        private static bool IsOptionalSlotRequested(
            NpcPartSlot slot,
            NpcCharacterSpec spec,
            HashSet<string> desiredTags,
            List<NpcPartEntry> entries)
        {
            if (!IsOptional(slot))
            {
                return true;
            }

            if (slot == NpcPartSlot.Weapon && Normalize(spec.appearance.weaponType) == "none")
            {
                return false;
            }

            return entries.Any(entry => entry.tags != null && entry.tags.Any(tag => desiredTags.Contains(Normalize(tag))));
        }

        private static bool IsOptional(NpcPartSlot slot)
        {
            return slot is NpcPartSlot.BackAccessory or NpcPartSlot.Hat or NpcPartSlot.Weapon or NpcPartSlot.FrontAccessory;
        }

        private static HashSet<string> BuildDesiredTags(NpcCharacterSpec spec)
        {
            NpcAppearanceSpec appearance = spec.appearance ?? new NpcAppearanceSpec();
            var tags = new[]
            {
                appearance.bodyType, appearance.skinTone, appearance.hairStyle, appearance.hairColor,
                appearance.outfitStyle, appearance.primaryColor, appearance.secondaryColor,
                appearance.weaponType, spec.occupation, spec.faction
            };

            var result = new HashSet<string>(tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(Normalize), StringComparer.OrdinalIgnoreCase);
            if (appearance.accessories != null)
            {
                result.UnionWith(appearance.accessories.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(Normalize));
            }
            return result;
        }

        private static HashSet<string> BuildPriorityTags(NpcCharacterSpec spec)
        {
            NpcAppearanceSpec appearance = spec.appearance ?? new NpcAppearanceSpec();
            return new HashSet<string>(new[]
            {
                appearance.bodyType,
                appearance.hairStyle,
                appearance.outfitStyle,
                appearance.weaponType
            }.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(Normalize), StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildFingerprint(IEnumerable<NpcResolvedPart> parts, int seed)
        {
            var builder = new StringBuilder();
            foreach (NpcResolvedPart part in parts)
            {
                builder.Append('|').Append(part.slot).Append(':').Append(part.partId);
            }
            return StableHash(builder.ToString()).ToString("X8");
        }

        private static string Normalize(string value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }

        private static float Next01(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFF) / 16777216f;
        }
    }
}
