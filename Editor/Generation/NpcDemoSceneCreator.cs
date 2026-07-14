using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Xiyue.AINpcGenerator.Editor
{
    public static class NpcDemoSceneCreator
    {
        private const string ScenePath = "Assets/XiyueGenerated/Demo/XiyueNpcDemo.unity";

        [MenuItem("Tools/Xiyue AI NPC/Create Demo Scene From Generated Characters")]
        public static void Create()
        {
            DefaultNpcAssetWriter.EnsureAssetFolder("Assets/XiyueGenerated/Demo");
            string outputRoot = NpcGeneratorProjectSettings.instance.outputRoot;
            string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { outputRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();
            string playerPath = prefabPaths.FirstOrDefault(path =>
                System.IO.Path.GetFileNameWithoutExtension(path).EndsWith("_Player", StringComparison.Ordinal));
            string[] npcPaths = prefabPaths.Where(path =>
                    System.IO.Path.GetFileNameWithoutExtension(path).EndsWith("_NPC", StringComparison.Ordinal))
                .Take(20)
                .ToArray();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            cameraObject.transform.position = new Vector3(5f, -3f, -10f);
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.13f);

            var npcRoot = new GameObject("GeneratedNPCs");
            SceneManager.MoveGameObjectToScene(npcRoot, scene);
            for (int index = 0; index < npcPaths.Length; index++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(npcPaths[index]);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetParent(npcRoot.transform, true);
                instance.transform.position = new Vector3((index % 6) * 1.6f, -(index / 6) * 1.6f, 0f);
            }

            if (!string.IsNullOrWhiteSpace(playerPath))
            {
                GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                player.name = "Player";
                player.transform.position = new Vector3(4f, -2f, 0f);
            }
            else
            {
                Debug.LogWarning("No generated _Player prefab was found. Generate a character first, then recreate the demo scene.");
            }

            var instructions = new GameObject("README - Move with arrows/WASD, interact with E or click NPC");
            SceneManager.MoveGameObjectToScene(instructions, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log($"Created demo scene at {ScenePath} with {npcPaths.Length} generated NPC prefabs.");
        }
    }
}
