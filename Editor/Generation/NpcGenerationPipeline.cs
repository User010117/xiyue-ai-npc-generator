using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Xiyue.AINpcGenerator.Editor
{
    public sealed class NpcNeedsReviewException : Exception
    {
        public NpcNeedsReviewException(string message) : base(message) { }
    }

    public sealed class NpcGeneratedAssets
    {
        public string folderPath;
        public string atlasPath;
        public string definitionPath;
        public string npcPrefabPath;
        public string playerPrefabPath;
        public NpcResolvedAppearance resolvedAppearance;
    }

    public interface INpcAssetWriter
    {
        NpcGeneratedAssets Write(
            NpcGenerationJob job,
            NpcRigProfile profile,
            NpcPartCatalog catalog,
            NpcPartResolution resolution,
            Texture2D atlas,
            Action<NpcGenerationStatus, float, string> progress);
    }

    public interface INpcQualityValidator
    {
        IReadOnlyList<string> Validate(NpcGeneratedAssets generated, NpcRigProfile profile);
    }

    public static class NpcGenerationPipeline
    {
        /// <summary>
        /// 把 Gemini 原始 Sprite Sheet 转为 Rig Profile 标准 Atlas，再复用现有资产 Writer 生成动画和预制体。
        /// </summary>
        public static NpcGeneratedAssets GenerateFromSpriteSheet(
            NpcGenerationJob job,
            NpcRigProfile profile,
            Action<NpcGenerationStatus, float, string> progress)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            string profileError = "未选择 Rig Profile";
            if (profile == null || !profile.IsValid(out profileError))
                throw new InvalidOperationException("Rig Profile 无效：" + profileError);
            if (string.IsNullOrWhiteSpace(job.generatedSpriteSheetPath))
                throw new InvalidOperationException("任务没有可转换的 Sprite Sheet 图片。");

            progress?.Invoke(NpcGenerationStatus.ConvertingSpriteSheet, 0.58f, "正在按 Rig Profile 转换并抠除绿幕");
            Texture2D atlas = SpriteSheetImageProcessor.BuildAtlas(job.generatedSpriteSheetPath, profile, job.greenScreenTolerance, out _);
            NpcPartCatalog virtualCatalog = ScriptableObject.CreateInstance<NpcPartCatalog>();
            virtualCatalog.catalogVersion = "sprite-sheet-v2";
            var resolution = new NpcPartResolution
            {
                resolvedAppearance = new NpcResolvedAppearance
                {
                    seed = job.seed,
                    fingerprint = SpriteSheetImageProcessor.ComputeFingerprint(atlas)
                }
            };
            NpcGeneratedAssets generated = null;
            try
            {
                var writer = new DefaultNpcAssetWriter();
                generated = writer.Write(job, profile, virtualCatalog, resolution, atlas, progress);
                progress?.Invoke(NpcGenerationStatus.QualityChecking, 0.94f, "正在验证动画和预制体");
                IReadOnlyList<string> errors = new GeneratedNpcQualityValidator().Validate(generated, profile);
                if (errors.Count > 0) throw new NpcNeedsReviewException(string.Join("\n", errors));
                DefaultNpcAssetWriter.WriteManifest(job, virtualCatalog, generated);
                return generated;
            }
            catch
            {
                DefaultNpcAssetWriter.DeleteGeneratedFolder(generated?.folderPath);
                throw;
            }
            finally
            {
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(virtualCatalog);
            }
        }

        public static NpcGeneratedAssets Generate(
            NpcGenerationJob job,
            NpcRigProfile profile,
            NpcPartCatalog catalog,
            Action<NpcGenerationStatus, float, string> progress,
            ISet<string> disallowedFingerprints = null)
        {
            NpcCatalogValidationReport catalogReport = NpcPartCatalogValidator.Validate(profile, catalog);
            if (!catalogReport.IsValid)
            {
                throw new NpcNeedsReviewException("Part catalog certification failed:\n" + string.Join("\n", catalogReport.errors));
            }

            progress?.Invoke(NpcGenerationStatus.SelectingParts, 0.48f, "Resolving compatible modular parts");
            INpcPartResolver resolver = new DeterministicNpcPartResolver();
            NpcPartResolution resolution = null;
            int selectedSeed = job.seed;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                selectedSeed = unchecked(job.seed + attempt * 104729);
                NpcPartResolution candidate = resolver.Resolve(job.character, catalog, selectedSeed);
                if (disallowedFingerprints == null || !disallowedFingerprints.Contains(candidate.resolvedAppearance.fingerprint))
                {
                    resolution = candidate;
                    break;
                }
            }

            if (resolution == null)
            {
                throw new NpcNeedsReviewException("Could not find a visually distinct compatible part combination after 16 deterministic attempts.");
            }
            job.seed = selectedSeed;
            if (!resolution.sourceParts.Any(part => part.slot == NpcPartSlot.Body) ||
                !resolution.sourceParts.Any(part => part.slot == NpcPartSlot.UpperOutfit))
            {
                throw new NpcNeedsReviewException("Resolved appearance does not contain all required slots.");
            }

            progress?.Invoke(NpcGenerationStatus.Assembling, 0.58f, "Compositing pixel atlas");
            INpcAssembler assembler = new NpcPixelAssembler();
            Texture2D atlas = assembler.Compose(profile, job.character, resolution.sourceParts);
            NpcGeneratedAssets generated = null;
            try
            {
                INpcAssetWriter writer = new DefaultNpcAssetWriter();
                generated = writer.Write(job, profile, catalog, resolution, atlas, progress);
                progress?.Invoke(NpcGenerationStatus.QualityChecking, 0.94f, "Validating generated Unity assets");
                INpcQualityValidator validator = new GeneratedNpcQualityValidator();
                IReadOnlyList<string> errors = validator.Validate(generated, profile);
                if (errors.Count > 0)
                {
                    throw new NpcNeedsReviewException(string.Join("\n", errors));
                }

                DefaultNpcAssetWriter.WriteManifest(job, catalog, generated);
                return generated;
            }
            catch
            {
                // Writer 返回后发生质量或 manifest 失败时也必须回滚，避免失败任务留下不可追踪的孤儿资产。
                DefaultNpcAssetWriter.DeleteGeneratedFolder(generated?.folderPath);
                throw;
            }
            finally
            {
                Object.DestroyImmediate(atlas);
            }
        }

        public static void AddGeneratedPrefabToScene(NpcGenerationJob job, bool asPlayer)
        {
            string definitionPath = job?.outputAssetPath;
            if (string.IsNullOrWhiteSpace(definitionPath))
            {
                return;
            }

            string folder = Path.GetDirectoryName(definitionPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            string suffix = asPlayer ? "_Player" : "_NPC";
            string prefabPath = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).EndsWith(suffix, StringComparison.Ordinal));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Could not find generated {suffix} prefab in {folder}.");
                return;
            }

            GameObject root = GameObject.Find("GeneratedNPCs");
            if (root == null)
            {
                root = new GameObject("GeneratedNPCs");
                Undo.RegisterCreatedObjectUndo(root, "Create Generated NPC root");
            }

            int index = root.transform.childCount;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Add generated character");
            Undo.SetTransformParent(instance.transform, root.transform, "Parent generated character");
            instance.transform.position = new Vector3((index % 8) * 1.5f, -(index / 8) * 1.5f, 0f);
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(instance.scene);
        }
    }

    /// <summary>负责保存、检查和规范化 AI Sprite Sheet；纯图片逻辑与队列/窗口解耦。</summary>
    internal static class SpriteSheetImageProcessor
    {
        /// <summary>把模型结果统一转为 PNG 资产并返回可持久化的项目路径。</summary>
        public static string SavePreview(NpcGenerationJob job, byte[] imageBytes)
        {
            if (job == null || imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("生成图片为空。", nameof(imageBytes));
            Texture2D source = LoadTexture(imageBytes);
            try
            {
                string folder = $"{NpcReferenceAssetStore.JobRoot}/{job.batchId}/{job.jobId}";
                DefaultNpcAssetWriter.EnsureAssetFolder(folder);
                string path = folder + "/SourceSpriteSheet.png";
                File.WriteAllBytes(DefaultNpcAssetWriter.AssetPathToAbsolute(path), source.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.filterMode = FilterMode.Point;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                return path;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>检查可自动验证的网格条件；警告不会阻止用户手动确认。</summary>
        public static string Inspect(string assetPath, NpcRigProfile profile, float greenTolerance)
        {
            Texture2D source = LoadAssetTexture(assetPath);
            Texture2D atlas = null;
            try
            {
                var warnings = new List<string>();
                if (source.width != source.height) warnings.Add($"原图不是 1:1（{source.width}×{source.height}）");
                if (source.width % profile.framesPerDirection != 0 || source.height % profile.directions != 0)
                    warnings.Add("原图尺寸不能被 Rig 网格整除，将使用点采样对齐");
                atlas = BuildAtlas(source, profile, greenTolerance, warnings);
                return string.Join("；", warnings.Distinct());
            }
            finally
            {
                if (atlas != null) Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>从项目图片构建标准 Atlas；源图顶部第一行映射到 Unity Atlas 的底部第零行。</summary>
        public static Texture2D BuildAtlas(string assetPath, NpcRigProfile profile, float greenTolerance, out string warning)
        {
            Texture2D source = LoadAssetTexture(assetPath);
            try
            {
                var warnings = new List<string>();
                Texture2D atlas = BuildAtlas(source, profile, greenTolerance, warnings);
                warning = string.Join("；", warnings.Distinct());
                return atlas;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>执行逐格最近邻采样，避免整图翻转导致角色上下颠倒。</summary>
        internal static Texture2D BuildAtlas(Texture2D source, NpcRigProfile profile, float greenTolerance, List<string> warnings)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            string error = "未选择 Rig Profile";
            if (profile == null || !profile.IsValid(out error)) throw new InvalidOperationException("Rig Profile 无效：" + error);
            float tolerance = Mathf.Clamp(greenTolerance, 0.05f, 0.8f);
            var atlas = new Texture2D(profile.AtlasWidth, profile.AtlasHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] sourcePixels = source.GetPixels32();
            var targetPixels = new Color32[profile.AtlasWidth * profile.AtlasHeight];
            var visiblePerCell = new int[profile.SpriteCount];

            for (int direction = 0; direction < profile.directions; direction++)
            {
                for (int frame = 0; frame < profile.framesPerDirection; frame++)
                {
                    int cellIndex = direction * profile.framesPerDirection + frame;
                    for (int localY = 0; localY < profile.frameHeight; localY++)
                    {
                        int sourceY = Mathf.Clamp(Mathf.FloorToInt(((profile.directions - direction - 1) + (localY + 0.5f) / profile.frameHeight) * source.height / profile.directions), 0, source.height - 1);
                        int targetY = direction * profile.frameHeight + localY;
                        for (int localX = 0; localX < profile.frameWidth; localX++)
                        {
                            int sourceX = Mathf.Clamp(Mathf.FloorToInt((frame + (localX + 0.5f) / profile.frameWidth) * source.width / profile.framesPerDirection), 0, source.width - 1);
                            Color32 color = sourcePixels[sourceY * source.width + sourceX];
                            if (IsGreenScreen(color, tolerance)) color.a = 0;
                            else if (color.a > 8) visiblePerCell[cellIndex]++;
                            int targetX = frame * profile.frameWidth + localX;
                            targetPixels[targetY * profile.AtlasWidth + targetX] = color;
                        }
                    }
                }
            }

            for (int index = 0; index < visiblePerCell.Length; index++)
            {
                if (visiblePerCell[index] == 0)
                    warnings?.Add($"网格 {index + 1} 在绿幕处理后没有可见像素");
            }
            atlas.SetPixels32(targetPixels);
            atlas.Apply(false, false);
            return atlas;
        }

        /// <summary>按与纯绿的距离和绿色优势共同判断，降低误删角色绿色服装的概率。</summary>
        internal static bool IsGreenScreen(Color32 color, float tolerance)
        {
            float r = color.r / 255f;
            float g = color.g / 255f;
            float b = color.b / 255f;
            float distance = Mathf.Sqrt(r * r + (1f - g) * (1f - g) + b * b) / 1.7320508f;
            return g > r + 0.12f && g > b + 0.12f && distance <= tolerance;
        }

        /// <summary>根据标准 Atlas 内容生成稳定短指纹，用于 Definition 和清单追溯。</summary>
        public static string ComputeFingerprint(Texture2D atlas)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(atlas.EncodeToPNG());
            return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 16).ToLowerInvariant();
        }

        /// <summary>从项目资产读取原始图片字节，不依赖 TextureImporter 的 Read/Write 设置。</summary>
        private static Texture2D LoadAssetTexture(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) throw new InvalidOperationException("Sprite Sheet 路径为空。");
            string absolute = DefaultNpcAssetWriter.AssetPathToAbsolute(assetPath);
            if (!File.Exists(absolute)) throw new FileNotFoundException("找不到 Sprite Sheet：" + assetPath, absolute);
            return LoadTexture(File.ReadAllBytes(absolute));
        }

        /// <summary>解码 PNG/JPEG；失败时立即释放临时纹理。</summary>
        private static Texture2D LoadTexture(byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(bytes, false)) return texture;
            Object.DestroyImmediate(texture);
            throw new InvalidDataException("模型返回的内容不是 Unity 可读取的图片。");
        }
    }

    /// <summary>不调用文本模型时创建足够运行现有 NPC 组件的本地基础数据。</summary>
    internal static class NpcLocalCharacterFactory
    {
        /// <summary>名称优先使用预设名，否则使用描述首行；其余字段使用可维护的确定性默认值。</summary>
        public static NpcCharacterSpec Create(string prompt, string presetName)
        {
            string firstLine = (prompt ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            string name = string.IsNullOrWhiteSpace(presetName) ? firstLine : presetName.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "AI NPC";
            if (name.Length > 48) name = name.Substring(0, 48);
            return new NpcCharacterSpec
            {
                displayName = name,
                biography = (prompt ?? string.Empty).Trim(),
                personalityTraits = new[] { "neutral", "quiet" },
                dialogueLines = new[] { "……" },
                movementStyle = "normal",
                behaviorTendency = "balanced",
                appearance = new NpcAppearanceSpec()
            };
        }
    }

    public sealed class DefaultNpcAssetWriter : INpcAssetWriter
    {
        public NpcGeneratedAssets Write(
            NpcGenerationJob job,
            NpcRigProfile profile,
            NpcPartCatalog catalog,
            NpcPartResolution resolution,
            Texture2D atlas,
            Action<NpcGenerationStatus, float, string> progress)
        {
            if (job == null || string.IsNullOrWhiteSpace(job.jobId))
            {
                throw new ArgumentException("Generation job requires a stable jobId.", nameof(job));
            }

            string outputRoot = NormalizeOutputRoot(job.outputRoot);
            string safeName = SanitizeName(job.character.displayName);
            string folder = string.Empty;
            string staging = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/XiyueAiNpcGenerator/Staging", job.jobId));
            string stagingPng = Path.Combine(staging, safeName + ".png");

            try
            {
                EnsureAssetFolder(outputRoot);
                folder = AssetDatabase.GenerateUniqueAssetPath($"{outputRoot}/{safeName}_{job.ShortJobId}");
                EnsureAssetFolder(folder);
                Directory.CreateDirectory(staging);
                File.WriteAllBytes(stagingPng, atlas.EncodeToPNG());

                var generated = new NpcGeneratedAssets
                {
                    folderPath = folder,
                    atlasPath = $"{folder}/{safeName}_Atlas.png",
                    definitionPath = $"{folder}/{safeName}_Definition.asset",
                    npcPrefabPath = $"{folder}/{safeName}_NPC.prefab",
                    playerPrefabPath = $"{folder}/{safeName}_Player.prefab",
                    resolvedAppearance = resolution.resolvedAppearance
                };
                progress?.Invoke(NpcGenerationStatus.Importing, 0.68f, "Importing generated atlas");
                File.Copy(stagingPng, AssetPathToAbsolute(generated.atlasPath), true);
                AssetDatabase.ImportAsset(generated.atlasPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureAndSliceAtlas(generated.atlasPath, profile, safeName);

                Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(generated.atlasPath)
                    .OfType<Sprite>()
                    .ToDictionary(sprite => sprite.name, sprite => sprite, StringComparer.Ordinal);
                if (sprites.Count != profile.SpriteCount)
                {
                    throw new InvalidOperationException($"Expected {profile.SpriteCount} sliced sprites, found {sprites.Count}.");
                }

                progress?.Invoke(NpcGenerationStatus.Importing, 0.78f, "Creating animations and character data");
                AnimationClip[] idleClips = new AnimationClip[profile.directions];
                AnimationClip[] walkClips = new AnimationClip[profile.directions];
                NpcDefinition definition = ScriptableObject.CreateInstance<NpcDefinition>();

                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int direction = 0; direction < profile.directions; direction++)
                    {
                        string directionName = profile.directionNames[direction];
                        idleClips[direction] = CreateIdleClip(profile, sprites, safeName, direction, directionName);
                        walkClips[direction] = CreateWalkClip(profile, sprites, safeName, direction, directionName);
                        AssetDatabase.CreateAsset(idleClips[direction], $"{folder}/{safeName}_Idle_{directionName}.anim");
                        AssetDatabase.CreateAsset(walkClips[direction], $"{folder}/{safeName}_Walk_{directionName}.anim");
                    }

                    float moveSpeed = job.character.movementStyle switch
                    {
                        "slow" => 0.9f,
                        "fast" => 2.4f,
                        _ => 1.5f
                    };
                    definition.Initialize(
                        job.jobId,
                        job.character,
                        job.seed,
                        job.prompt,
                        job.model,
                        catalog.catalogVersion,
                        resolution.resolvedAppearance,
                        sprites[SpriteName(safeName, 0, 0, profile)],
                        idleClips.Concat(walkClips).ToArray(),
                        moveSpeed);
                    AssetDatabase.CreateAsset(definition, generated.definitionPath);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                AssetDatabase.SaveAssets();
                AnimatorController controller = CreateAnimatorController(folder, safeName, idleClips, walkClips);
                CreatePrefabs(generated, safeName, definition, controller, sprites[SpriteName(safeName, 0, 0, profile)]);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return generated;
            }
            catch
            {
                DeleteGeneratedFolder(folder);
                throw;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(staging))
                    {
                        Directory.Delete(staging, true);
                    }
                }
                catch (Exception cleanupException)
                {
                    // staging 清理失败不应覆盖已经生成成功或更有价值的原始异常，留给下次生成覆盖/人工清理。
                    Debug.LogWarning("Could not clean NPC staging directory: " + cleanupException.Message);
                }
            }
        }

        public static void WriteManifest(NpcGenerationJob job, NpcPartCatalog catalog, NpcGeneratedAssets generated)
        {
            var manifest = new NpcGenerationManifest
            {
                manifestVersion = "1.1",
                jobId = job.jobId,
                createdUtc = DateTime.UtcNow.ToString("O"),
                model = job.model,
                prompt = job.prompt,
                seed = job.seed,
                catalogVersion = catalog.catalogVersion,
                referenceImageGuids = job.referenceImageGuids ?? Array.Empty<string>(),
                appearanceFingerprint = generated.resolvedAppearance.fingerprint,
                partIds = generated.resolvedAppearance.parts.Select(part => part.partId).ToArray(),
                definitionPath = generated.definitionPath,
                npcPrefabPath = generated.npcPrefabPath,
                playerPrefabPath = generated.playerPrefabPath
            };
            string path = generated.folderPath + "/generation-manifest.json";
            File.WriteAllText(AssetPathToAbsolute(path), JsonUtility.ToJson(manifest, true));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// 删除一次生成事务创建的完整文件夹；空路径和重复回滚都安全忽略。
        /// </summary>
        internal static void DeleteGeneratedFolder(string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder) && AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void ConfigureAndSliceAtlas(string path, NpcRigProfile profile, string safeName)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = profile.pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider dataProvider = factories.GetSpriteEditorDataProviderFromObject(importer);
            dataProvider.InitSpriteEditorDataProvider();
            var rectangles = new List<SpriteRect>();
            var namePairs = new List<SpriteNameFileIdPair>();
            for (int direction = 0; direction < profile.directions; direction++)
            {
                for (int frame = 0; frame < profile.framesPerDirection; frame++)
                {
                    var spriteId = UnityEditor.GUID.Generate();
                    string name = SpriteName(safeName, direction, frame, profile);
                    rectangles.Add(new SpriteRect
                    {
                        name = name,
                        rect = new Rect(
                            frame * profile.frameWidth,
                            direction * profile.frameHeight,
                            profile.frameWidth,
                            profile.frameHeight),
                        alignment = SpriteAlignment.Custom,
                        pivot = profile.pivot,
                        spriteID = spriteId
                    });
                    namePairs.Add(new SpriteNameFileIdPair(name, spriteId));
                }
            }

            dataProvider.SetSpriteRects(rectangles.ToArray());
            ISpriteNameFileIdDataProvider nameProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameProvider?.SetNameFileIdPairs(namePairs);
            dataProvider.Apply();
            importer.SaveAndReimport();
        }

        private static AnimationClip CreateIdleClip(
            NpcRigProfile profile,
            IReadOnlyDictionary<string, Sprite> sprites,
            string safeName,
            int direction,
            string directionName)
        {
            var clip = new AnimationClip { name = $"{safeName}_Idle_{directionName}", frameRate = profile.animationFrameRate };
            SetSpriteCurve(clip, new[]
            {
                new ObjectReferenceKeyframe
                {
                    time = 0f,
                    value = sprites[SpriteName(safeName, direction, 0, profile)]
                }
            });
            SetLoop(clip, true);
            return clip;
        }

        private static AnimationClip CreateWalkClip(
            NpcRigProfile profile,
            IReadOnlyDictionary<string, Sprite> sprites,
            string safeName,
            int direction,
            string directionName)
        {
            var clip = new AnimationClip { name = $"{safeName}_Walk_{directionName}", frameRate = profile.animationFrameRate };
            var frames = new ObjectReferenceKeyframe[profile.framesPerDirection];
            for (int frame = 0; frame < profile.framesPerDirection; frame++)
            {
                frames[frame] = new ObjectReferenceKeyframe
                {
                    time = frame / profile.animationFrameRate,
                    value = sprites[SpriteName(safeName, direction, frame, profile)]
                };
            }
            SetSpriteCurve(clip, frames);
            SetLoop(clip, true);
            return clip;
        }

        private static void SetSpriteCurve(AnimationClip clip, ObjectReferenceKeyframe[] frames)
        {
            var binding = new EditorCurveBinding
            {
                path = string.Empty,
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, frames);
        }

        private static void SetLoop(AnimationClip clip, bool loop)
        {
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        private static AnimatorController CreateAnimatorController(
            string folder,
            string safeName,
            AnimationClip[] idleClips,
            AnimationClip[] walkClips)
        {
            string path = $"{folder}/{safeName}_Animator.controller";
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Direction", AnimatorControllerParameterType.Int);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState firstIdle = null;

            for (int direction = 0; direction < idleClips.Length; direction++)
            {
                AnimatorState idle = stateMachine.AddState(idleClips[direction].name);
                idle.motion = idleClips[direction];
                firstIdle ??= idle;
                AnimatorStateTransition idleTransition = stateMachine.AddAnyStateTransition(idle);
                ConfigureTransition(idleTransition);
                idleTransition.AddCondition(AnimatorConditionMode.Less, 0.01f, "Speed");
                idleTransition.AddCondition(AnimatorConditionMode.Equals, direction, "Direction");

                AnimatorState walk = stateMachine.AddState(walkClips[direction].name);
                walk.motion = walkClips[direction];
                AnimatorStateTransition walkTransition = stateMachine.AddAnyStateTransition(walk);
                ConfigureTransition(walkTransition);
                walkTransition.AddCondition(AnimatorConditionMode.Greater, 0.01f, "Speed");
                walkTransition.AddCondition(AnimatorConditionMode.Equals, direction, "Direction");
            }

            stateMachine.defaultState = firstIdle;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void ConfigureTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.canTransitionToSelf = false;
        }

        private static void CreatePrefabs(
            NpcGeneratedAssets generated,
            string safeName,
            NpcDefinition definition,
            RuntimeAnimatorController controller,
            Sprite preview)
        {
            GameObject npc = CreateBaseCharacter(safeName + "_NPC", definition, controller, preview);
            NpcInteractionBubble bubble = CreateBubble(npc.transform);
            npc.AddComponent<NpcBrain2D>().Configure(definition, bubble);
            npc.AddComponent<NpcClickableInteraction>();
            PrefabUtility.SaveAsPrefabAsset(npc, generated.npcPrefabPath);
            Object.DestroyImmediate(npc);

            GameObject player = CreateBaseCharacter(safeName + "_Player", definition, controller, preview);
            player.AddComponent<TopDownPlayerController>();
            player.AddComponent<NpcProximityInteractor>();
            PrefabUtility.SaveAsPrefabAsset(player, generated.playerPrefabPath);
            Object.DestroyImmediate(player);
        }

        private static GameObject CreateBaseCharacter(
            string name,
            NpcDefinition definition,
            RuntimeAnimatorController controller,
            Sprite preview)
        {
            var root = new GameObject(name);
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = preview;
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            var body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            var collider = root.AddComponent<CapsuleCollider2D>();
            collider.size = new Vector2(0.45f, 0.65f);
            collider.offset = new Vector2(0f, 0.32f);
            root.AddComponent<NpcGeneratedMarker>().SetId(definition.NpcId);
            return root;
        }

        private static NpcInteractionBubble CreateBubble(Transform character)
        {
            var anchor = new GameObject("InteractionBubble");
            anchor.transform.SetParent(character, false);
            anchor.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(anchor.transform, false);
            var text = new GameObject("Text");
            text.transform.SetParent(visual.transform, false);
            var textMesh = text.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.LowerCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.07f;
            textMesh.fontSize = 32;
            textMesh.color = Color.white;
            textMesh.text = "...";
            var bubble = anchor.AddComponent<NpcInteractionBubble>();
            bubble.Configure(visual, textMesh);
            return bubble;
        }

        private static string SpriteName(string safeName, int direction, int frame, NpcRigProfile profile)
        {
            return $"{safeName}_{profile.directionNames[direction]}_{frame:00}";
        }

        private static string NormalizeOutputRoot(string value)
        {
            string path = string.IsNullOrWhiteSpace(value) ? "Assets/XiyueGenerated/NPCs" : value.Trim().Replace('\\', '/').TrimEnd('/');
            return path.StartsWith("Assets/", StringComparison.Ordinal) && !path.Split('/').Contains("..")
                ? path
                : "Assets/XiyueGenerated/NPCs";
        }

        internal static void EnsureAssetFolder(string path)
        {
            string[] segments = path.Replace('\\', '/').Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }
                current = next;
            }
        }

        internal static string AssetPathToAbsolute(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static string SanitizeName(string value)
        {
            string source = string.IsNullOrWhiteSpace(value) ? "Npc" : value.Trim();
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            string result = new(source.Where(character => !invalid.Contains(character) && character != '/' && character != '\\').ToArray());
            result = result.Replace(' ', '_');
            return string.IsNullOrWhiteSpace(result) ? "Npc" : result.Substring(0, Math.Min(result.Length, 40));
        }
    }

    public sealed class GeneratedNpcQualityValidator : INpcQualityValidator
    {
        public IReadOnlyList<string> Validate(NpcGeneratedAssets generated, NpcRigProfile profile)
        {
            var errors = new List<string>();
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(generated.atlasPath).OfType<Sprite>().ToArray();
            if (sprites.Length != profile.SpriteCount)
            {
                errors.Add($"Sprite quality check failed: expected {profile.SpriteCount}, found {sprites.Length}.");
            }

            NpcDefinition definition = AssetDatabase.LoadAssetAtPath<NpcDefinition>(generated.definitionPath);
            if (definition == null || definition.PreviewSprite == null)
            {
                errors.Add("NpcDefinition or its preview sprite is missing.");
            }
            else if (definition.GeneratedClips == null || definition.GeneratedClips.Length != profile.directions * 2)
            {
                errors.Add("NpcDefinition does not reference all idle and walk clips.");
            }

            ValidatePrefab(generated.npcPrefabPath, typeof(NpcBrain2D), errors);
            ValidatePrefab(generated.playerPrefabPath, typeof(TopDownPlayerController), errors);
            return errors;
        }

        private static void ValidatePrefab(string path, Type requiredComponent, List<string> errors)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                errors.Add($"Generated prefab is missing: {path}");
                return;
            }

            if (prefab.GetComponent(requiredComponent) == null)
            {
                errors.Add($"Prefab '{prefab.name}' is missing {requiredComponent.Name}.");
            }

            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) > 0)
            {
                errors.Add($"Prefab '{prefab.name}' contains a missing script.");
            }

            Animator animator = prefab.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                errors.Add($"Prefab '{prefab.name}' has no valid Animator Controller.");
            }
        }
    }

    [Serializable]
    internal sealed class NpcGenerationManifest
    {
        public string manifestVersion;
        public string jobId;
        public string createdUtc;
        public string model;
        public string prompt;
        public int seed;
        public string catalogVersion;
        /// <summary>记录生成任务实际冻结的参考图资产 GUID，便于追溯但不泄露图片字节。</summary>
        public string[] referenceImageGuids;
        public string appearanceFingerprint;
        public string[] partIds;
        public string definitionPath;
        public string npcPrefabPath;
        public string playerPrefabPath;
    }
}
