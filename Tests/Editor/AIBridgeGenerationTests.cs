using System.IO;
using AIBridge.Internal.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor.Tests
{
    public class AIBridgeGenerationTests
    {
        private const string TestRoot = "Assets/AIBridgeGeneratedTests";

        [TearDown]
        public void TearDown()
        {
            AIBridgeGeneration.DeleteAssetIfExists(TestRoot);
            AIBridgeGeneration.RefreshAssets();
        }

        [Test]
        public void EnsureFolder_IsIdempotent()
        {
            var folder = TestRoot + "/Nested/Folder";

            AIBridgeGeneration.EnsureFolder(folder);
            AIBridgeGeneration.EnsureFolder(folder);

            Assert.That(AssetDatabase.IsValidFolder(folder), Is.True);
        }

        [Test]
        public void AddOrGetComponent_DoesNotDuplicateExistingComponent()
        {
            var gameObject = new GameObject("GenerationTest");
            try
            {
                var first = AIBridgeGeneration.AddOrGetComponent<BoxCollider>(gameObject);
                var second = AIBridgeGeneration.AddOrGetComponent<BoxCollider>(gameObject);

                Assert.That(first, Is.SameAs(second));
                Assert.That(gameObject.GetComponents<BoxCollider>().Length, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LoadOrCreateMaterial_CreatesAssetAndTracksResult()
        {
            var result = new AIBridgeGenerationResult();
            var materialPath = TestRoot + "/Materials/Test.mat";

            var material = AIBridgeGeneration.LoadOrCreateMaterial(materialPath, "Standard", result);

            Assert.That(material != null, Is.True);
            Assert.That(File.Exists(materialPath), Is.True);
            CollectionAssert.Contains(result.assets, materialPath);
        }

        [Test]
        public void SavePrefab_CreatesPrefabAndTracksResult()
        {
            var result = new AIBridgeGenerationResult();
            var prefabPath = TestRoot + "/Prefabs/Test.prefab";
            var root = new GameObject("GeneratedPrefab");

            try
            {
                AIBridgeGeneration.SavePrefab(root, prefabPath, result);

                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null, Is.True);
                CollectionAssert.Contains(result.prefabs, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GenerationResult_SerializesToJson()
        {
            var result = new AIBridgeGenerationResult()
                .AddAsset(TestRoot + "/A.mat")
                .AddAsset(TestRoot + "/A.mat")
                .AddPrefab(TestRoot + "/A.prefab")
                .AddWarning("warning")
                .AddMessage("done");

            var json = AIBridgeJson.Serialize(result, true);

            StringAssert.Contains("\"assets\"", json);
            StringAssert.Contains("\"prefabs\"", json);
            StringAssert.Contains("\"warnings\"", json);
            StringAssert.Contains("\"messages\"", json);
            Assert.That(result.assets.Count, Is.EqualTo(1));
        }
    }
}
