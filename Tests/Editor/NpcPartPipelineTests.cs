using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Xiyue.AINpcGenerator.Editor;

namespace Xiyue.AINpcGenerator.Tests
{
    public sealed class NpcPartPipelineTests
    {
        private readonly List<Object> cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in cleanup)
            {
                Object.DestroyImmediate(item);
            }
            cleanup.Clear();
        }

        [Test]
        public void Resolve_IsDeterministicForSameSeed()
        {
            NpcPartCatalog catalog = CreateCatalog();
            var spec = new NpcCharacterSpec
            {
                appearance = new NpcAppearanceSpec
                {
                    bodyType = "slim",
                    outfitStyle = "robe",
                    hairStyle = "long",
                    weaponType = "none"
                }
            };
            var resolver = new DeterministicNpcPartResolver();

            NpcPartResolution first = resolver.Resolve(spec, catalog, 123456);
            NpcPartResolution second = resolver.Resolve(spec, catalog, 123456);

            Assert.That(first.resolvedAppearance.fingerprint, Is.EqualTo(second.resolvedAppearance.fingerprint));
            Assert.That(first.resolvedAppearance.parts.Count, Is.EqualTo(second.resolvedAppearance.parts.Count));
            for (int index = 0; index < first.resolvedAppearance.parts.Count; index++)
            {
                Assert.That(first.resolvedAppearance.parts[index].partId, Is.EqualTo(second.resolvedAppearance.parts[index].partId));
            }
        }

        [Test]
        public void Compose_ProducesExpectedAtlasAndVisiblePixels()
        {
            NpcRigProfile profile = CreateProfile(8, 8, 1);
            Texture2D texture = CreateSolidAtlas(profile);
            var spec = new NpcCharacterSpec { appearance = new NpcAppearanceSpec { skinTone = "pale" } };
            var part = new NpcPartEntry { partId = "body", slot = NpcPartSlot.Body, atlas = texture, tintMode = NpcPartTintMode.Skin };
            Texture2D output = new NpcPixelAssembler().Compose(profile, spec, new[] { part });
            cleanup.Add(output);

            Assert.That(output.width, Is.EqualTo(8));
            Assert.That(output.height, Is.EqualTo(32));
            Assert.That(output.GetPixel(0, 0).a, Is.GreaterThan(0.9f));
        }

        [Test]
        public void CatalogValidator_RequiresCertifiedFallbacks()
        {
            NpcRigProfile profile = CreateProfile(8, 8, 1);
            Texture2D texture = CreateSolidAtlas(profile);
            NpcPartCatalog catalog = ScriptableObject.CreateInstance<NpcPartCatalog>();
            cleanup.Add(catalog);
            catalog.parts.Add(new NpcPartEntry { partId = "only-body", slot = NpcPartSlot.Body, atlas = texture, isFallback = true });

            NpcCatalogValidationReport report = NpcPartCatalogValidator.Validate(profile, catalog);

            Assert.That(report.IsValid, Is.False);
            Assert.That(report.errors.Exists(item => item.Contains("UpperOutfit")), Is.True);
        }

        [TestCase("blue")]
        [TestCase("#FF0000")]
        public void ColorParser_RecognizesNamedAndHexColors(string value)
        {
            Color result = NpcColorParser.Parse(value, Color.magenta);
            Assert.That(result, Is.Not.EqualTo(Color.magenta));
        }

        private NpcPartCatalog CreateCatalog()
        {
            NpcRigProfile profile = CreateProfile(8, 8, 1);
            Texture2D texture = CreateSolidAtlas(profile);
            NpcPartCatalog catalog = ScriptableObject.CreateInstance<NpcPartCatalog>();
            cleanup.Add(catalog);
            catalog.parts.Add(new NpcPartEntry { partId = "body-default", slot = NpcPartSlot.Body, atlas = texture, isFallback = true, tags = new[] { "average" } });
            catalog.parts.Add(new NpcPartEntry { partId = "body-slim", slot = NpcPartSlot.Body, atlas = texture, tags = new[] { "slim" } });
            catalog.parts.Add(new NpcPartEntry { partId = "tunic", slot = NpcPartSlot.UpperOutfit, atlas = texture, isFallback = true, tags = new[] { "casual" } });
            catalog.parts.Add(new NpcPartEntry { partId = "robe", slot = NpcPartSlot.UpperOutfit, atlas = texture, tags = new[] { "robe" } });
            catalog.parts.Add(new NpcPartEntry { partId = "hair-long-a", slot = NpcPartSlot.HairFront, atlas = texture, isFallback = true, tags = new[] { "long" } });
            catalog.parts.Add(new NpcPartEntry { partId = "hair-long-b", slot = NpcPartSlot.HairFront, atlas = texture, tags = new[] { "long" } });
            return catalog;
        }

        private NpcRigProfile CreateProfile(int width, int height, int frames)
        {
            NpcRigProfile profile = ScriptableObject.CreateInstance<NpcRigProfile>();
            cleanup.Add(profile);
            profile.frameWidth = width;
            profile.frameHeight = height;
            profile.framesPerDirection = frames;
            profile.directions = 4;
            profile.directionNames = new[] { "Down", "Left", "Right", "Up" };
            return profile;
        }

        private Texture2D CreateSolidAtlas(NpcRigProfile profile)
        {
            var texture = new Texture2D(profile.AtlasWidth, profile.AtlasHeight, TextureFormat.RGBA32, false);
            cleanup.Add(texture);
            var pixels = new Color[texture.width * texture.height];
            for (int index = 0; index < pixels.Length; index++) pixels[index] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
