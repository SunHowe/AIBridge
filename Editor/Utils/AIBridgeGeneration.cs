using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIBridge.Editor
{
    /// <summary>
    /// 一次性 Editor 生成脚本辅助 API，供 Roslyn 临时脚本稳定创建复杂资源。
    /// </summary>
    public static class AIBridgeGeneration
    {
        private const string AssetsPrefix = "Assets/";
        private const string AssetsRoot = "Assets";

        public static void EnsureFolder(string assetFolderPath)
        {
            var normalized = NormalizeAssetPath(assetFolderPath);
            ValidateAssetsPath(normalized, "assetFolderPath");
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            var parts = normalized.Split('/');
            var current = AssetsRoot;
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        public static bool DeleteAssetIfExists(string assetPath)
        {
            var normalized = NormalizeAssetPath(assetPath);
            ValidateAssetsPath(normalized, "assetPath");
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(normalized) == null
                && !AssetDatabase.IsValidFolder(normalized))
            {
                return false;
            }

            return AssetDatabase.DeleteAsset(normalized);
        }

        public static Material LoadOrCreateMaterial(string materialPath, string shaderName)
        {
            return LoadOrCreateMaterial(materialPath, shaderName, null);
        }

        public static Material LoadOrCreateMaterial(string materialPath, string shaderName, AIBridgeGenerationResult result)
        {
            var normalized = NormalizeAssetPath(materialPath);
            ValidateAssetsPath(normalized, "materialPath");
            if (!normalized.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("materialPath must end with .mat");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(normalized);
            if (material != null)
            {
                if (result != null)
                {
                    result.AddAsset(normalized);
                }

                return material;
            }

            EnsureParentFolder(normalized);
            var shader = Shader.Find(string.IsNullOrEmpty(shaderName) ? "Standard" : shaderName);
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                throw new InvalidOperationException("Shader not found: " + shaderName);
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, normalized);
            if (result != null)
            {
                result.AddAsset(normalized);
            }

            return material;
        }

        public static T AddOrGetComponent<T>(GameObject gameObject) where T : Component
        {
            if (gameObject == null)
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            var component = gameObject.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            return gameObject.AddComponent<T>();
        }

        public static Component AddOrGetComponent(GameObject gameObject, Type componentType)
        {
            if (gameObject == null)
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            if (componentType == null)
            {
                throw new ArgumentNullException(nameof(componentType));
            }

            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                throw new ArgumentException("componentType must derive from Component");
            }

            var component = gameObject.GetComponent(componentType);
            if (component != null)
            {
                return component;
            }

            return gameObject.AddComponent(componentType);
        }

        public static GameObject SavePrefab(GameObject root, string prefabPath)
        {
            return SavePrefab(root, prefabPath, null);
        }

        public static GameObject SavePrefab(GameObject root, string prefabPath, AIBridgeGenerationResult result)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            var normalized = NormalizeAssetPath(prefabPath);
            ValidateAssetsPath(normalized, "prefabPath");
            if (!normalized.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("prefabPath must end with .prefab");
            }

            EnsureParentFolder(normalized);
            bool success;
            var savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, normalized, out success);
            if (!success || savedPrefab == null)
            {
                throw new InvalidOperationException("Failed to save prefab: " + normalized);
            }

            if (result != null)
            {
                result.AddPrefab(normalized);
            }

            return savedPrefab;
        }

        public static void SaveScene(Scene scene, string scenePath)
        {
            SaveScene(scene, scenePath, null);
        }

        public static void SaveScene(Scene scene, string scenePath, AIBridgeGenerationResult result)
        {
            var normalized = NormalizeAssetPath(scenePath);
            ValidateAssetsPath(normalized, "scenePath");
            if (!normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("scenePath must end with .unity");
            }

            EnsureParentFolder(normalized);
            if (!EditorSceneManager.SaveScene(scene, normalized))
            {
                throw new InvalidOperationException("Failed to save scene: " + normalized);
            }

            if (result != null)
            {
                result.AddScene(normalized);
            }
        }

        public static void MarkDirty(UnityEngine.Object target)
        {
            if (target != null)
            {
                EditorUtility.SetDirty(target);
            }
        }

        public static void RefreshAssets()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureParentFolder(string assetPath)
        {
            var parent = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent.Replace('\\', '/'));
            }
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : assetPath.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static void ValidateAssetsPath(string assetPath, string argumentName)
        {
            if (string.IsNullOrEmpty(assetPath)
                || assetPath == AssetsRoot
                || !assetPath.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase)
                || assetPath.Contains("/../")
                || assetPath.EndsWith("/..", StringComparison.Ordinal))
            {
                throw new ArgumentException(argumentName + " must be a project asset path under Assets/.");
            }
        }
    }

    [Serializable]
    public sealed class AIBridgeGenerationResult
    {
        public readonly List<string> assets = new List<string>();
        public readonly List<string> prefabs = new List<string>();
        public readonly List<string> scenes = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> messages = new List<string>();

        public AIBridgeGenerationResult AddAsset(string path)
        {
            AddUnique(assets, path);
            return this;
        }

        public AIBridgeGenerationResult AddPrefab(string path)
        {
            AddUnique(prefabs, path);
            return this;
        }

        public AIBridgeGenerationResult AddScene(string path)
        {
            AddUnique(scenes, path);
            return this;
        }

        public AIBridgeGenerationResult AddWarning(string message)
        {
            AddUnique(warnings, message);
            return this;
        }

        public AIBridgeGenerationResult AddMessage(string message)
        {
            AddUnique(messages, message);
            return this;
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value) || list.Contains(value))
            {
                return;
            }

            list.Add(value);
        }
    }
}
