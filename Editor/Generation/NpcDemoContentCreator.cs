using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Xiyue.AINpcGenerator.Editor
{
    public static class NpcDemoContentCreator
    {
        private const string Root = "Assets/XiyueGenerated/Demo";
        private const string PartRoot = Root + "/Parts";
        private const string ProfilePath = Root + "/DemoNpcRigProfile.asset";
        private const string CatalogPath = Root + "/DemoNpcPartCatalog.asset";

        private enum DemoShape
        {
            Body,
            Lower,
            Upper,
            Hair,
            Hat,
            Weapon,
            Back,
            Front
        }

        [MenuItem("Tools/Xiyue AI NPC/Create Demo Content")]
        public static void CreateFromMenu()
        {
            CreateOrUpdate(out NpcRigProfile profile, out NpcPartCatalog catalog);
            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
            Debug.Log($"Created Xiyue AI NPC demo profile '{profile.name}' and catalog '{catalog.name}'.");
        }

        public static void CreateOrUpdate(out NpcRigProfile profile, out NpcPartCatalog catalog)
        {
            DefaultNpcAssetWriter.EnsureAssetFolder(PartRoot);
            profile = AssetDatabase.LoadAssetAtPath<NpcRigProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<NpcRigProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.frameWidth = 48;
            profile.frameHeight = 48;
            profile.pixelsPerUnit = 48;
            profile.directions = 4;
            profile.framesPerDirection = 4;
            profile.animationFrameRate = 8f;
            profile.pivot = new Vector2(0.5f, 0.08f);
            profile.directionNames = new[] { "Down", "Left", "Right", "Up" };
            EditorUtility.SetDirty(profile);
            NpcRigProfile activeProfile = profile;

            catalog = AssetDatabase.LoadAssetAtPath<NpcPartCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<NpcPartCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.catalogVersion = "demo-1.0";
            catalog.parts = new List<NpcPartEntry>
            {
                Part("body_average", NpcPartSlot.Body, DemoShape.Body, 0, NpcPartTintMode.Skin, true, "average"),
                Part("body_slim", NpcPartSlot.Body, DemoShape.Body, 1, NpcPartTintMode.Skin, false, "slim", "thin"),
                Part("body_strong", NpcPartSlot.Body, DemoShape.Body, 2, NpcPartTintMode.Skin, false, "strong", "muscular", "large"),

                Part("lower_trousers", NpcPartSlot.LowerOutfit, DemoShape.Lower, 0, NpcPartTintMode.SecondaryOutfit, true, "casual", "traveler", "merchant", "guard"),
                Part("lower_robe", NpcPartSlot.LowerOutfit, DemoShape.Lower, 1, NpcPartTintMode.SecondaryOutfit, false, "robe", "scholar", "mage", "ancient"),

                Part("upper_tunic", NpcPartSlot.UpperOutfit, DemoShape.Upper, 0, NpcPartTintMode.PrimaryOutfit, true, "casual", "traveler", "merchant", "villager"),
                Part("upper_robe", NpcPartSlot.UpperOutfit, DemoShape.Upper, 1, NpcPartTintMode.PrimaryOutfit, false, "robe", "scholar", "mage", "ancient"),
                Part("upper_armor", NpcPartSlot.UpperOutfit, DemoShape.Upper, 2, NpcPartTintMode.PrimaryOutfit, false, "armor", "light_armor", "guard", "warrior", "soldier"),

                Part("hair_short", NpcPartSlot.HairFront, DemoShape.Hair, 0, NpcPartTintMode.Hair, true, "short", "cropped"),
                Part("hair_long", NpcPartSlot.HairFront, DemoShape.Hair, 1, NpcPartTintMode.Hair, false, "long", "straight"),
                Part("hair_ponytail", NpcPartSlot.HairFront, DemoShape.Hair, 2, NpcPartTintMode.Hair, false, "ponytail", "tied"),

                Part("hat_hood", NpcPartSlot.Hat, DemoShape.Hat, 0, NpcPartTintMode.SecondaryOutfit, false, "hood", "robe", "mysterious"),
                Part("hat_cap", NpcPartSlot.Hat, DemoShape.Hat, 1, NpcPartTintMode.SecondaryOutfit, false, "cap", "merchant", "worker"),

                Part("weapon_sword", NpcPartSlot.Weapon, DemoShape.Weapon, 0, NpcPartTintMode.None, false, "sword", "blade", "warrior", "guard"),
                Part("weapon_staff", NpcPartSlot.Weapon, DemoShape.Weapon, 1, NpcPartTintMode.None, false, "staff", "wand", "mage", "scholar"),

                Part("back_cape", NpcPartSlot.BackAccessory, DemoShape.Back, 0, NpcPartTintMode.SecondaryOutfit, false, "cape", "cloak"),
                Part("front_satchel", NpcPartSlot.FrontAccessory, DemoShape.Front, 0, NpcPartTintMode.SecondaryOutfit, false, "bag", "satchel", "merchant", "traveler")
            };
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            NpcCatalogValidationReport report = NpcPartCatalogValidator.Validate(profile, catalog);
            if (!report.IsValid)
            {
                throw new InvalidOperationException("Generated demo catalog failed certification:\n" + string.Join("\n", report.errors));
            }

            NpcGeneratorProjectSettings settings = NpcGeneratorProjectSettings.instance;
            settings.rigProfileGuid = AssetDatabase.AssetPathToGUID(ProfilePath);
            settings.partCatalogGuid = AssetDatabase.AssetPathToGUID(CatalogPath);
            settings.Persist();

            NpcPartEntry Part(
                string id,
                NpcPartSlot slot,
                DemoShape shape,
                int variant,
                NpcPartTintMode tint,
                bool fallback,
                params string[] tags)
            {
                return new NpcPartEntry
                {
                    partId = id,
                    slot = slot,
                    atlas = CreateTexture(activeProfile, id, shape, variant),
                    tintMode = tint,
                    isFallback = fallback,
                    weight = 1f,
                    tags = tags,
                    incompatibleTags = Array.Empty<string>()
                };
            }
        }

        private static Texture2D CreateTexture(NpcRigProfile profile, string id, DemoShape shape, int variant)
        {
            var texture = new Texture2D(profile.AtlasWidth, profile.AtlasHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[profile.AtlasWidth * profile.AtlasHeight];
            var ink = shape == DemoShape.Weapon
                ? new Color32(170, 180, 195, 255)
                : new Color32(255, 255, 255, 255);

            for (int direction = 0; direction < profile.directions; direction++)
            {
                for (int frame = 0; frame < profile.framesPerDirection; frame++)
                {
                    DrawPart(pixels, profile, direction, frame, shape, variant, ink);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            string assetPath = $"{PartRoot}/{id}.png";
            File.WriteAllBytes(DefaultNpcAssetWriter.AssetPathToAbsolute(assetPath), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static void DrawPart(
            Color32[] pixels,
            NpcRigProfile profile,
            int direction,
            int frame,
            DemoShape shape,
            int variant,
            Color32 color)
        {
            int baseX = frame * profile.frameWidth;
            int baseY = direction * profile.frameHeight;
            int stride = profile.AtlasWidth;
            int step = frame == 1 ? -1 : frame == 3 ? 1 : 0;

            void Pixel(int x, int y, Color32 value)
            {
                if (x >= 0 && x < profile.frameWidth && y >= 0 && y < profile.frameHeight)
                {
                    pixels[(baseY + y) * stride + baseX + x] = value;
                }
            }

            void Rect(int left, int bottom, int width, int height, Color32 value)
            {
                for (int y = bottom; y < bottom + height; y++)
                for (int x = left; x < left + width; x++)
                    Pixel(x, y, value);
            }

            void Circle(int centerX, int centerY, int radius, Color32 value)
            {
                int radiusSquared = radius * radius;
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                    if (x * x + y * y <= radiusSquared) Pixel(centerX + x, centerY + y, value);
            }

            switch (shape)
            {
                case DemoShape.Body:
                    int bodyWidth = variant == 1 ? 8 : variant == 2 ? 14 : 11;
                    Circle(24, 31, 6, color);
                    Rect(24 - bodyWidth / 2, 17, bodyWidth, 12, color);
                    Rect(17, 18, 3, 9, color);
                    Rect(29, 18, 3, 9, color);
                    Rect(20 + step, 6, 4, 12, color);
                    Rect(25 - step, 6, 4, 12, color);
                    break;
                case DemoShape.Lower:
                    if (variant == 1)
                    {
                        Rect(18, 7, 13, 12, color);
                        Rect(16, 7, 17, 4, color);
                    }
                    else
                    {
                        Rect(20 + step, 7, 4, 11, color);
                        Rect(25 - step, 7, 4, 11, color);
                    }
                    break;
                case DemoShape.Upper:
                    Rect(18, 17, 13, 11, color);
                    Rect(16, 19, 3, variant == 2 ? 8 : 6, color);
                    Rect(30, 19, 3, variant == 2 ? 8 : 6, color);
                    if (variant == 1) Rect(17, 14, 15, 5, color);
                    if (variant == 2)
                    {
                        Rect(16, 25, 5, 4, color);
                        Rect(28, 25, 5, 4, color);
                    }
                    break;
                case DemoShape.Hair:
                    Rect(18, 34, 13, 4, color);
                    Rect(17, 30, 4, variant == 0 ? 5 : 11, color);
                    Rect(28, 30, 4, variant == 0 ? 5 : 11, color);
                    if (variant == 1) Rect(direction == 1 ? 27 : 18, 22, 4, 12, color);
                    if (variant == 2) Rect(direction == 2 ? 29 : 16, 25, 4, 10, color);
                    break;
                case DemoShape.Hat:
                    Rect(17, 36, 15, 3, color);
                    Rect(20, 39, variant == 0 ? 9 : 12, variant == 0 ? 5 : 3, color);
                    break;
                case DemoShape.Weapon:
                    int weaponX = direction == 1 ? 14 : direction == 2 ? 34 : 33;
                    Rect(weaponX, 13, 2 + variant, variant == 0 ? 20 : 22, color);
                    Rect(weaponX - 2, 15, 6, 2, color);
                    break;
                case DemoShape.Back:
                    Rect(direction == 2 ? 17 : 14, 12, 7, 17, color);
                    break;
                case DemoShape.Front:
                    Rect(direction == 1 ? 17 : 28, 14, 6, 7, color);
                    Rect(direction == 1 ? 19 : 30, 21, 2, 7, color);
                    break;
            }
        }
    }
}
