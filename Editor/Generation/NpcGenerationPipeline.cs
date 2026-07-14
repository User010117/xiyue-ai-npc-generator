using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            try
            {
                INpcAssetWriter writer = new DefaultNpcAssetWriter();
                NpcGeneratedAssets generated = writer.Write(job, profile, catalog, resolution, atlas, progress);
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
            string outputRoot = NormalizeOutputRoot(job.outputRoot);
            EnsureAssetFolder(outputRoot);
            string safeName = SanitizeName(job.character.displayName);
            string folder = AssetDatabase.GenerateUniqueAssetPath($"{outputRoot}/{safeName}_{job.jobId.Substring(0, 8)}");
            EnsureAssetFolder(folder);

            string staging = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/XiyueAiNpcGenerator/Staging", job.jobId));
            Directory.CreateDirectory(staging);
            string stagingPng = Path.Combine(staging, safeName + ".png");
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

            try
            {
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
                AssetDatabase.DeleteAsset(folder);
                throw;
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
        }

        public static void WriteManifest(NpcGenerationJob job, NpcPartCatalog catalog, NpcGeneratedAssets generated)
        {
            var manifest = new NpcGenerationManifest
            {
                manifestVersion = "1.0",
                jobId = job.jobId,
                createdUtc = DateTime.UtcNow.ToString("O"),
                model = job.model,
                prompt = job.prompt,
                seed = job.seed,
                catalogVersion = catalog.catalogVersion,
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
        public string appearanceFingerprint;
        public string[] partIds;
        public string definitionPath;
        public string npcPrefabPath;
        public string playerPrefabPath;
    }
}
