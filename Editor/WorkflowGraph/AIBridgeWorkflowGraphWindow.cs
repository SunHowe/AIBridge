using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    public sealed class AIBridgeWorkflowGraphWindow : EditorWindow
    {
        private const float ContentTopOffset = 88f;
        private const float InspectorMinWidth = 280f;
        private const float InspectorMaxWidth = 340f;
        private const float InspectorWidthRatio = 0.24f;
        private const float SplitterWidth = 1f;

        private enum ViewMode
        {
            Routing,
            BranchDetail,
            Recipe,
            Run
        }

        private readonly WorkflowGraphRenderer _renderer = new WorkflowGraphRenderer();
        private readonly WorkflowGraphInspector _inspector = new WorkflowGraphInspector();
        private WorkflowGraphLoader _loader;
        private WorkflowGraphDocument _document;
        private List<WorkflowRecipeSummary> _recipes = new List<WorkflowRecipeSummary>();
        private WorkflowRunSummaryData _activeRunSummary;
        private ViewMode _viewMode;
        private int _selectedRecipeIndex;
        private string _selectedNodeId;
        private string _selectedBranchDetailId = "implementation";
        private Vector2 _pan = new Vector2(24f, 24f);
        private float _zoom = 1f;
        private bool _draggingCanvas;
        private Vector2 _dragStartMouse;
        private Vector2 _dragStartPan;

        [MenuItem("AIBridge/Workflow Graph")]
        public static void OpenWindow()
        {
            var window = GetWindow<AIBridgeWorkflowGraphWindow>();
            window.titleContent = new GUIContent(AIBridgeEditorText.T("AIBridge Workflow Graph", "AIBridge Workflow Graph"));
            window.minSize = new Vector2(980f, 580f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshAll();
        }

        private void OnGUI()
        {
            if (_loader == null)
            {
                RefreshAll();
            }

            DrawToolbar();
            DrawHeader();

            var contentRect = new Rect(0f, ContentTopOffset, position.width, position.height - ContentTopOffset);
            var inspectorWidth = Mathf.Clamp(position.width * InspectorWidthRatio, InspectorMinWidth, InspectorMaxWidth);
            var graphRect = new Rect(
                contentRect.x,
                contentRect.y,
                Mathf.Max(240f, contentRect.width - inspectorWidth - SplitterWidth),
                contentRect.height);
            var splitterRect = new Rect(graphRect.xMax, contentRect.y, SplitterWidth, contentRect.height);
            var inspectorRect = new Rect(splitterRect.xMax, contentRect.y, inspectorWidth, contentRect.height);

            DrawGraph(graphRect);
            EditorGUI.DrawRect(splitterRect, EditorGUIUtility.isProSkin
                ? new Color(0.12f, 0.12f, 0.12f, 1f)
                : new Color(0.72f, 0.72f, 0.72f, 1f));
            DrawInspector(inspectorRect);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(AIBridgeEditorText.T("Refresh", "刷新"), EditorStyles.toolbarButton, GUILayout.Width(76f)))
            {
                RefreshAll();
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Reset View", "重置视图"), EditorStyles.toolbarButton, GUILayout.Width(92f)))
            {
                _pan = new Vector2(24f, 24f);
                _zoom = 1f;
            }

            GUILayout.Space(10f);
            EditorGUI.BeginChangeCheck();
            _viewMode = (ViewMode)GUILayout.Toolbar((int)_viewMode, new[]
            {
                "Routing",
                "Branch",
                "Recipe",
                "Run"
            }, EditorStyles.toolbarButton, GUILayout.Width(250f));
            if (EditorGUI.EndChangeCheck())
            {
                RebuildDocument();
            }

            GUILayout.FlexibleSpace();
            DrawBranchSelector();
            DrawRecipeSelector();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                _document == null ? "Workflow Graph" : _document.Title + " - " + _document.Subtitle,
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                _document == null ? string.Empty : _document.Summary,
                EditorStyles.wordWrappedMiniLabel);
            if (_activeRunSummary != null)
            {
                EditorGUILayout.LabelField(
                    "Active Run: " + _activeRunSummary.RunId + " / " + _activeRunSummary.Status,
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRecipeSelector()
        {
            if (_viewMode != ViewMode.Recipe)
            {
                return;
            }

            if (_recipes == null || _recipes.Count == 0)
            {
                EditorGUILayout.LabelField("No recipes", EditorStyles.miniLabel, GUILayout.Width(140f));
                return;
            }

            var names = new string[_recipes.Count];
            for (var i = 0; i < _recipes.Count; i++)
            {
                var recipe = _recipes[i];
                names[i] = recipe.Source + "/" + recipe.DisplayName;
            }

            EditorGUI.BeginChangeCheck();
            _selectedRecipeIndex = EditorGUILayout.Popup(_selectedRecipeIndex, names, GUILayout.Width(300f));
            if (EditorGUI.EndChangeCheck())
            {
                RebuildDocument();
            }
        }

        private void DrawBranchSelector()
        {
            if (_viewMode != ViewMode.BranchDetail)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            _selectedBranchDetailId = EditorGUILayout.Popup(
                _selectedBranchDetailId == "implementation" ? 0 : 0,
                new[] { "implementation/Implementation Branch" },
                GUILayout.Width(260f)) == 0 ? "implementation" : "implementation";
            if (EditorGUI.EndChangeCheck())
            {
                RebuildDocument();
            }
        }

        private void DrawGraph(Rect graphRect)
        {
            HandleGraphInput(graphRect);

            var clickedNode = _renderer.Draw(_document, graphRect, _pan, _zoom, _selectedNodeId);
            if (clickedNode != null)
            {
                HandleNodeSelection(clickedNode);
                Repaint();
            }

            DrawGraphOverlay(graphRect);
        }

        private void DrawInspector(Rect inspectorRect)
        {
            GUILayout.BeginArea(inspectorRect, EditorStyles.helpBox);
            _inspector.Draw(GetSelectedNode(), _document);
            GUILayout.EndArea();
        }

        private void DrawGraphOverlay(Rect graphRect)
        {
            GUILayout.BeginArea(new Rect(graphRect.x + 8f, graphRect.y + 8f, 230f, 54f), EditorStyles.helpBox);
            EditorGUILayout.LabelField("Zoom: " + _zoom.ToString("F2"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(AIBridgeEditorText.T("Drag background to pan. Scroll to zoom.", "拖动画布平移，滚轮缩放。"), EditorStyles.wordWrappedMiniLabel);
            GUILayout.EndArea();
        }

        private void HandleGraphInput(Rect graphRect)
        {
            var current = Event.current;
            if (!graphRect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.ScrollWheel)
            {
                var oldZoom = _zoom;
                _zoom = Mathf.Clamp(_zoom - current.delta.y * 0.03f, 0.55f, 1.6f);
                if (!Mathf.Approximately(oldZoom, _zoom))
                {
                    current.Use();
                    Repaint();
                }
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                _draggingCanvas = true;
                _dragStartMouse = current.mousePosition;
                _dragStartPan = _pan;
            }
            else if (current.type == EventType.MouseDrag && _draggingCanvas && current.button == 0)
            {
                _pan = _dragStartPan + (current.mousePosition - _dragStartMouse) / Mathf.Max(0.01f, _zoom);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp)
            {
                _draggingCanvas = false;
            }
        }

        private WorkflowGraphNode GetSelectedNode()
        {
            return _document == null ? null : _document.FindNode(_selectedNodeId);
        }

        private void HandleNodeSelection(WorkflowGraphNode clickedNode)
        {
            if (clickedNode == null)
            {
                return;
            }

            _selectedNodeId = clickedNode.Id;
            if (_viewMode == ViewMode.Routing && string.Equals(clickedNode.Id, "branch-implementation"))
            {
                _selectedBranchDetailId = "implementation";
                _viewMode = ViewMode.BranchDetail;
                _pan = new Vector2(24f, 24f);
                _zoom = 1f;
                RebuildDocument();
            }
        }

        private void RefreshAll()
        {
            _loader = new WorkflowGraphLoader(GetProjectRoot());
            _recipes = _loader.ListRecipes();
            _activeRunSummary = _loader.LoadActiveRunSummary();
            _selectedRecipeIndex = Mathf.Clamp(_selectedRecipeIndex, 0, Mathf.Max(0, _recipes.Count - 1));
            RebuildDocument();
        }

        private void RebuildDocument()
        {
            if (_loader == null)
            {
                return;
            }

            switch (_viewMode)
            {
                case ViewMode.BranchDetail:
                    _document = _loader.LoadBranchDetailGraph(_selectedBranchDetailId);
                    break;
                case ViewMode.Recipe:
                    var recipe = _recipes != null && _recipes.Count > 0 ? _recipes[Mathf.Clamp(_selectedRecipeIndex, 0, _recipes.Count - 1)] : null;
                    _document = _loader.LoadRecipeGraph(recipe);
                    break;
                case ViewMode.Run:
                    _document = _loader.LoadActiveRunGraph();
                    break;
                default:
                    _document = _loader.LoadRoutingGraph();
                    break;
            }

            WorkflowGraphLayout.Layout(_document);
            if (_document != null && _document.FindNode(_selectedNodeId) == null)
            {
                _selectedNodeId = _document.Nodes.Count > 0 ? _document.Nodes[0].Id : null;
            }

            Repaint();
        }

        private static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }
    }
}
