using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xiyue.AINpcGenerator.Editor;

namespace Xiyue.AINpcGenerator.Tests
{
    public sealed class NpcPartPipelineTests
    {
        private readonly List<Object> cleanup = new();
        /// <summary>测试创建的临时 Unity 资产根目录。</summary>
        private readonly List<string> assetCleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in cleanup)
            {
                Object.DestroyImmediate(item);
            }
            cleanup.Clear();
            foreach (string path in assetCleanup)
            {
                if (AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            }
            assetCleanup.Clear();
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

        /// <summary>
        /// 验证不兼容 fallback 不会遮蔽同槽位的兼容候选，防止必选层被错误判定缺失。
        /// </summary>
        [Test]
        public void Resolve_UsesCompatiblePartWhenFallbackConflicts()
        {
            NpcRigProfile profile = CreateProfile(8, 8, 1);
            Texture2D texture = CreateSolidAtlas(profile);
            NpcPartCatalog catalog = ScriptableObject.CreateInstance<NpcPartCatalog>();
            cleanup.Add(catalog);
            catalog.parts.Add(new NpcPartEntry
            {
                partId = "body-conflicting-fallback",
                slot = NpcPartSlot.Body,
                atlas = texture,
                isFallback = true,
                incompatibleTags = new[] { "slim" }
            });
            catalog.parts.Add(new NpcPartEntry
            {
                partId = "body-compatible",
                slot = NpcPartSlot.Body,
                atlas = texture,
                tags = new[] { "average" }
            });
            catalog.parts.Add(new NpcPartEntry
            {
                partId = "upper-default",
                slot = NpcPartSlot.UpperOutfit,
                atlas = texture,
                isFallback = true
            });
            var spec = new NpcCharacterSpec
            {
                appearance = new NpcAppearanceSpec { bodyType = "slim", outfitStyle = "casual" }
            };

            NpcPartResolution result = new DeterministicNpcPartResolver().Resolve(spec, catalog, 7);

            Assert.That(result.resolvedAppearance.parts.Exists(part => part.partId == "body-compatible"), Is.True);
            Assert.That(result.resolvedAppearance.parts.Exists(part => part.partId == "body-conflicting-fallback"), Is.False);
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

        /// <summary>验证 AI 顶部第一行映射到 Unity Atlas 第零方向，同时不对像素做平滑混色。</summary>
        [Test]
        public void SpriteSheetProcessor_ReordersTopRowsWithoutFlippingFrames()
        {
            NpcRigProfile profile = CreateProfile(8, 8, 1);
            var source = new Texture2D(8, 32, TextureFormat.RGBA32, false);
            cleanup.Add(source);
            Color32[] pixels = new Color32[8 * 32];
            Color32[] bottomToTop = { Color.blue, Color.green, Color.yellow, Color.red };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 8; x++) pixels[y * 8 + x] = bottomToTop[y / 8];
            source.SetPixels32(pixels);
            source.Apply();

            Texture2D atlas = SpriteSheetImageProcessor.BuildAtlas(source, profile, 0.05f, new List<string>());
            cleanup.Add(atlas);

            Color32[] atlasPixels = atlas.GetPixels32();
            Assert.That(atlasPixels[0], Is.EqualTo((Color32)Color.red));
            Assert.That(atlasPixels[8 * atlas.width], Is.EqualTo((Color32)Color.yellow));
        }

        /// <summary>验证绿幕容差能删除纯绿背景，但不会误删普通蓝色像素。</summary>
        [Test]
        public void SpriteSheetProcessor_GreenScreenToleranceIsSelective()
        {
            Assert.That(SpriteSheetImageProcessor.IsGreenScreen(new Color32(0, 255, 0, 255), 0.2f), Is.True);
            Assert.That(SpriteSheetImageProcessor.IsGreenScreen(new Color32(20, 80, 220, 255), 0.8f), Is.False);
        }

        /// <summary>验证预设保存后使用独立副本，删除原图不会让托管引用消失。</summary>
        [Test]
        public void PresetReferenceStore_CopiesAndDeletesManagedAssets()
        {
            const string sourceRoot = "Assets/XiyueGenerated/TestPresetReferenceSource";
            const string sourcePath = sourceRoot + "/source.png";
            assetCleanup.Add(sourceRoot);
            DefaultNpcAssetWriter.EnsureAssetFolder(sourceRoot);
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            cleanup.Add(texture);
            File.WriteAllBytes(DefaultNpcAssetWriter.AssetPathToAbsolute(sourcePath), texture.EncodeToPNG());
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            string presetId = System.Guid.NewGuid().ToString("N");

            string[] managed = NpcReferenceAssetStore.ReplacePresetReferences(presetId, "测试预设", new[] { sourceGuid });
            AssetDatabase.DeleteAsset(sourceRoot);

            Assert.That(managed, Has.Length.EqualTo(1));
            Assert.That(managed[0], Is.Not.EqualTo(sourceGuid));
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(managed[0])), Is.Not.Null);
            Assert.That(NpcReferenceAssetStore.DeletePresetReferences(presetId, out string error), Is.True, error);
        }

        /// <summary>验证确认 Sprite Sheet 后能一次生成切片、八个动画、控制器、定义和双预制体。</summary>
        [Test]
        public void GenerateFromSpriteSheet_CreatesCompleteNpcAssets()
        {
            const string inputRoot = "Assets/XiyueGenerated/TestSpriteSheetInput";
            const string outputRoot = "Assets/XiyueGenerated/TestSpriteSheetOutput";
            const string inputPath = inputRoot + "/source.png";
            assetCleanup.Add(inputRoot);
            assetCleanup.Add(outputRoot);
            DefaultNpcAssetWriter.EnsureAssetFolder(inputRoot);

            NpcRigProfile profile = CreateProfile(8, 8, 2);
            var source = new Texture2D(16, 32, TextureFormat.RGBA32, false);
            cleanup.Add(source);
            var pixels = new Color32[source.width * source.height];
            for (int index = 0; index < pixels.Length; index++) pixels[index] = new Color32(0, 255, 0, 255);
            // 每个单元格保留一块不透明角色像素，确保绿幕去除后仍可生成有效预览。
            for (int row = 0; row < 4; row++)
                for (int frame = 0; frame < 2; frame++)
                    for (int y = 2; y < 6; y++)
                        for (int x = 2; x < 6; x++) pixels[(row * 8 + y) * source.width + frame * 8 + x] = new Color32(40, 80, 220, 255);
            source.SetPixels32(pixels);
            source.Apply();
            File.WriteAllBytes(DefaultNpcAssetWriter.AssetPathToAbsolute(inputPath), source.EncodeToPNG());
            AssetDatabase.ImportAsset(inputPath, ImportAssetOptions.ForceSynchronousImport);

            var job = new NpcGenerationJob
            {
                jobId = System.Guid.NewGuid().ToString("N"),
                batchId = System.Guid.NewGuid().ToString("N"),
                prompt = "蓝甲测试角色",
                model = SpriteSheetGenerationDefaults.ImageModel,
                seed = 42,
                outputRoot = outputRoot,
                generatedSpriteSheetPath = inputPath,
                greenScreenTolerance = 0.2f,
                character = NpcLocalCharacterFactory.Create("测试预设", "蓝甲测试角色")
            };

            NpcGeneratedAssets generated = NpcGenerationPipeline.GenerateFromSpriteSheet(job, profile, null);
            NpcDefinition definition = AssetDatabase.LoadAssetAtPath<NpcDefinition>(generated.definitionPath);

            Assert.That(AssetDatabase.LoadAllAssetsAtPath(generated.atlasPath), Has.Exactly(profile.SpriteCount).InstanceOf<Sprite>());
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.GeneratedClips, Has.Length.EqualTo(8));
            Assert.That(definition.ModelName, Is.EqualTo(SpriteSheetGenerationDefaults.ImageModel));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(generated.npcPrefabPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(generated.playerPrefabPath), Is.Not.Null);
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
