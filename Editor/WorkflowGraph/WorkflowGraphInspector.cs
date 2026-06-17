using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    internal sealed class WorkflowGraphInspector
    {
        private Vector2 _scroll;

        public void Draw(WorkflowGraphNode node, WorkflowGraphDocument document)
        {
            EditorGUILayout.LabelField(AIBridgeEditorText.T("Inspector", "Inspector"), EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (node == null)
            {
                EditorGUILayout.HelpBox(
                    AIBridgeEditorText.T("Select a node to inspect details.", "选择一个节点查看详情。"),
                    MessageType.Info);
                DrawDocumentSummary(document);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField(node.Title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(node.Subtitle ?? string.Empty, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
            DrawInfo("Kind", node.Kind.ToString());
            DrawInfo("State", node.State.ToString());
            DrawInfo("Status", node.StatusText);
            DrawInfo("Role", node.SemanticRole);
            DrawInfo("Optional", node.IsOptional ? "True" : "False");

            if (!string.IsNullOrEmpty(node.Description))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(AIBridgeEditorText.T("Description", "说明"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(node.Description, EditorStyles.wordWrappedLabel);
            }

            if (node.Details.Count > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(AIBridgeEditorText.T("Details", "详情"), EditorStyles.boldLabel);
                for (var i = 0; i < node.Details.Count; i++)
                {
                    var detail = node.Details[i];
                    DrawInfo(detail.Label, detail.Value);
                }
            }

            if (!string.IsNullOrEmpty(node.SourcePath))
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(AIBridgeEditorText.T("Source", "来源"), EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(node.SourcePath, EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(34f));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(AIBridgeEditorText.T("Open", "打开")))
                {
                    OpenPath(document, node.SourcePath);
                }

                if (GUILayout.Button(AIBridgeEditorText.T("Reveal", "定位")))
                {
                    RevealPath(document, node.SourcePath);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawDocumentSummary(WorkflowGraphDocument document)
        {
            if (document == null)
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(document.Title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(document.Summary ?? string.Empty, EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrEmpty(document.Warning))
            {
                EditorGUILayout.HelpBox(document.Warning, MessageType.Warning);
            }
        }

        private static void DrawInfo(string label, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(value, EditorStyles.miniLabel, GUILayout.MinHeight(16f));
            GUILayout.Space(2f);
        }

        private static void OpenPath(WorkflowGraphDocument document, string path)
        {
            var fullPath = ResolvePath(document, path);
            if (File.Exists(fullPath))
            {
                AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(ToAssetPath(fullPath)));
                return;
            }

            if (Directory.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
            }
        }

        private static void RevealPath(WorkflowGraphDocument document, string path)
        {
            var fullPath = ResolvePath(document, path);
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
            }
        }

        private static string ResolvePath(WorkflowGraphDocument document, string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
            {
                return path;
            }

            if (document != null && !string.IsNullOrEmpty(document.SourceRootPath))
            {
                var rootedPath = Path.Combine(document.SourceRootPath, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(rootedPath) || Directory.Exists(rootedPath))
                {
                    return rootedPath;
                }
            }

            return Path.Combine(Path.GetDirectoryName(Application.dataPath), path.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ToAssetPath(string fullPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot) && fullPath.StartsWith(projectRoot))
            {
                return fullPath.Substring(projectRoot.Length + 1).Replace('\\', '/');
            }

            return fullPath;
        }
    }
}
