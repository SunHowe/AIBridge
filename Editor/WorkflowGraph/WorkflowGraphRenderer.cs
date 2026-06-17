using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    internal sealed class WorkflowGraphRenderer
    {
        private static readonly Color GraphBackgroundColor = new Color(0.17f, 0.18f, 0.20f, 1f);
        private static readonly Color MajorGridColor = new Color(0.85f, 0.88f, 0.92f, 0.09f);
        private static readonly Color MinorGridColor = new Color(0.85f, 0.88f, 0.92f, 0.035f);
        private static readonly Color EdgeColor = new Color(0.38f, 0.60f, 0.86f, 0.70f);
        private static readonly Color EdgeLabelBackgroundColor = new Color(0.15f, 0.16f, 0.18f, 0.92f);
        private static readonly Color OptionalNodeFillColor = new Color(0.36f, 0.33f, 0.18f, 0.98f);
        private static readonly Color OptionalNodeOutlineColor = new Color(0.95f, 0.83f, 0.42f, 0.95f);
        private static readonly Color OptionalEdgeColor = new Color(0.90f, 0.80f, 0.45f, 0.78f);
        private static readonly Color TitleTextColor = new Color(0.94f, 0.96f, 0.99f, 1f);
        private static readonly Color SecondaryTextColor = new Color(0.82f, 0.85f, 0.90f, 1f);
        private static readonly Color MutedTextColor = new Color(0.63f, 0.69f, 0.76f, 1f);
        private static readonly Color SelectedOutlineColor = new Color(0.18f, 0.52f, 0.98f, 1f);

        public WorkflowGraphNode Draw(WorkflowGraphDocument document, Rect viewport, Vector2 pan, float zoom, string selectedNodeId)
        {
            if (document == null)
            {
                return null;
            }

            EditorGUI.DrawRect(viewport, GraphBackgroundColor);
            DrawGrid(viewport, pan, zoom);
            var clickedNode = DrawNodesAndEdges(document, viewport, pan, zoom, selectedNodeId);
            return clickedNode;
        }

        private static WorkflowGraphNode DrawNodesAndEdges(WorkflowGraphDocument document, Rect viewport, Vector2 pan, float zoom, string selectedNodeId)
        {
            GUI.BeginGroup(viewport);
            var localViewport = new Rect(0f, 0f, viewport.width, viewport.height);

            Handles.BeginGUI();
            for (var i = 0; i < document.Edges.Count; i++)
            {
                DrawEdge(document, document.Edges[i], localViewport, pan, zoom);
            }
            Handles.EndGUI();

            WorkflowGraphNode clickedNode = null;
            for (var i = 0; i < document.Nodes.Count; i++)
            {
                var node = document.Nodes[i];
                var rect = ToScreenRect(node.Rect, localViewport, pan, zoom);
                DrawNode(rect, node, node.Id == selectedNodeId, zoom);
                // GUI.BeginGroup(viewport) 之后，mousePosition 已经是 group 内局部坐标，不能再次减 viewport。
                var localMousePosition = Event.current.mousePosition;
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(localMousePosition))
                {
                    clickedNode = node;
                    Event.current.Use();
                }
            }

            GUI.EndGroup();
            return clickedNode;
        }

        private static void DrawEdge(WorkflowGraphDocument document, WorkflowGraphEdge edge, Rect viewport, Vector2 pan, float zoom)
        {
            var from = document.FindNode(edge.FromNodeId);
            var to = document.FindNode(edge.ToNodeId);
            if (from == null || to == null)
            {
                return;
            }

            var fromRect = ToScreenRect(from.Rect, viewport, pan, zoom);
            var toRect = ToScreenRect(to.Rect, viewport, pan, zoom);
            var start = GetEdgeStart(fromRect, edge.Kind, from, to);
            var end = GetEdgeEnd(toRect, edge.Kind, from, to);
            var path = BuildEdgePath(fromRect, toRect, edge.Kind, start, end);
            Handles.color = GetEdgeColor(edge.Kind);
            if (edge.Kind == WorkflowGraphEdgeKind.OptionalFlow)
            {
                DrawDashedPolyline(path, 10f, 6f);
            }
            else
            {
                Handles.DrawAAPolyLine(2.2f, path);
            }

            DrawArrow(path[path.Length - 1], path[path.Length - 2], edge.Kind);
            if (!string.IsNullOrEmpty(edge.Label) && zoom >= 0.9f)
            {
                var labelPosition = GetPolylineMidpoint(path);
                var size = EditorStyles.miniLabel.CalcSize(new GUIContent(edge.Label));
                var labelRect = new Rect(labelPosition.x - size.x * 0.5f - 4f, labelPosition.y - 14f, size.x + 8f, 18f);
                EditorGUI.DrawRect(labelRect, EdgeLabelBackgroundColor);
                var previousColor = GUI.color;
                GUI.color = SecondaryTextColor;
                GUI.Label(labelRect, edge.Label, EditorStyles.miniLabel);
                GUI.color = previousColor;
            }
        }

        private static void DrawArrow(Vector3 end, Vector3 previous, WorkflowGraphEdgeKind edgeKind)
        {
            var direction = (end - previous).normalized;
            if (direction == Vector3.zero)
            {
                direction = Vector3.right;
            }

            var normal = new Vector3(-direction.y, direction.x, 0f);
            var p1 = end;
            var p2 = end - direction * 10f + normal * 4f;
            var p3 = end - direction * 10f - normal * 4f;
            Handles.color = GetEdgeColor(edgeKind);
            Handles.DrawAAConvexPolygon(p1, p2, p3);
        }

        private static void DrawNode(Rect rect, WorkflowGraphNode node, bool selected, float zoom)
        {
            var fillColor = node.IsOptional ? OptionalNodeFillColor : GetNodeColor(node.State);
            var outlineColor = node.IsOptional ? OptionalNodeOutlineColor : GetNodeOutlineColor(node.State);
            EditorGUI.DrawRect(rect, fillColor);

            Handles.BeginGUI();
            Handles.color = outlineColor;
            if (node.IsOptional)
            {
                DrawDashedRect(rect, 8f, 5f);
            }
            else
            {
                Handles.DrawAAPolyLine(
                    1.5f,
                    new Vector3(rect.xMin, rect.yMin),
                    new Vector3(rect.xMax, rect.yMin),
                    new Vector3(rect.xMax, rect.yMax),
                    new Vector3(rect.xMin, rect.yMax),
                    new Vector3(rect.xMin, rect.yMin));
            }
            if (selected)
            {
                Handles.color = SelectedOutlineColor;
                Handles.DrawAAPolyLine(
                    2.6f,
                    new Vector3(rect.xMin + 1f, rect.yMin + 1f),
                    new Vector3(rect.xMax - 1f, rect.yMin + 1f),
                    new Vector3(rect.xMax - 1f, rect.yMax - 1f),
                    new Vector3(rect.xMin + 1f, rect.yMax - 1f),
                    new Vector3(rect.xMin + 1f, rect.yMin + 1f));
            }
            Handles.EndGUI();

            var contentRect = new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, rect.height - 20f);
            DrawNodeLine(new Rect(contentRect.x, contentRect.y, contentRect.width, 22f), node.Title, EditorStyles.boldLabel, TitleTextColor);
            if (zoom >= 0.95f)
            {
                DrawNodeLine(new Rect(contentRect.x, contentRect.y + 24f, contentRect.width, 18f), node.Subtitle ?? string.Empty, EditorStyles.label, SecondaryTextColor);
                var kindText = node.IsOptional ? AIBridgeEditorText.T("Optional", "可选") : node.Kind.ToString();
                DrawNodeLine(new Rect(contentRect.x, contentRect.y + 50f, contentRect.width, 16f), kindText, EditorStyles.miniLabel, MutedTextColor);
                DrawNodeLine(new Rect(contentRect.x, contentRect.y + 66f, contentRect.width, 16f), node.StatusText, EditorStyles.miniLabel, MutedTextColor);
                return;
            }

            if (zoom >= 0.78f)
            {
                DrawNodeLine(new Rect(contentRect.x, contentRect.y + 28f, contentRect.width, 18f), node.Subtitle ?? string.Empty, EditorStyles.label, SecondaryTextColor);
                DrawNodeLine(new Rect(contentRect.x, contentRect.y + 54f, contentRect.width, 16f), node.StatusText, EditorStyles.miniLabel, MutedTextColor);
                return;
            }

            if (zoom >= 0.62f)
            {
                DrawNodeLine(new Rect(contentRect.x, contentRect.y + 30f, contentRect.width, 16f), node.StatusText, EditorStyles.miniLabel, MutedTextColor);
            }
        }

        private static void DrawGrid(Rect viewport, Vector2 pan, float zoom)
        {
            Handles.BeginGUI();
            DrawGridLines(viewport, pan, zoom, 96f, MajorGridColor, 1.1f);
            DrawGridLines(viewport, pan, zoom, 24f, MinorGridColor, 1f);

            Handles.EndGUI();
        }

        private static void DrawGridLines(Rect viewport, Vector2 pan, float zoom, float baseSpacing, Color color, float thickness)
        {
            Handles.color = color;
            var spacing = Mathf.Max(8f, baseSpacing * zoom);
            var xOffset = Mathf.Repeat(pan.x * zoom, spacing);
            for (var x = viewport.x + xOffset; x < viewport.xMax; x += spacing)
            {
                Handles.DrawAAPolyLine(thickness, new Vector3(x, viewport.y), new Vector3(x, viewport.yMax));
            }

            var yOffset = Mathf.Repeat(pan.y * zoom, spacing);
            for (var y = viewport.y + yOffset; y < viewport.yMax; y += spacing)
            {
                Handles.DrawAAPolyLine(thickness, new Vector3(viewport.x, y), new Vector3(viewport.xMax, y));
            }
        }

        private static Rect ToScreenRect(Rect rect, Rect viewport, Vector2 pan, float zoom)
        {
            return new Rect(
                (rect.x + pan.x) * zoom,
                (rect.y + pan.y) * zoom,
                rect.width * zoom,
                rect.height * zoom);
        }

        private static void DrawDashedRect(Rect rect, float dashLength, float gapLength)
        {
            DrawDashedLine(new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin), dashLength, gapLength);
            DrawDashedLine(new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax), dashLength, gapLength);
            DrawDashedLine(new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax), dashLength, gapLength);
            DrawDashedLine(new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMin, rect.yMin), dashLength, gapLength);
        }

        private static void DrawDashedPolyline(Vector3[] points, float dashLength, float gapLength)
        {
            if (points == null || points.Length < 2)
            {
                return;
            }

            for (var i = 1; i < points.Length; i++)
            {
                DrawDashedLine(points[i - 1], points[i], dashLength, gapLength);
            }
        }

        private static void DrawDashedLine(Vector3 start, Vector3 end, float dashLength, float gapLength)
        {
            var distance = Vector3.Distance(start, end);
            if (distance <= Mathf.Epsilon)
            {
                return;
            }

            var direction = (end - start).normalized;
            var step = dashLength + gapLength;
            var travelled = 0f;
            while (travelled < distance)
            {
                var segmentStart = start + direction * travelled;
                var segmentEnd = start + direction * Mathf.Min(travelled + dashLength, distance);
                Handles.DrawAAPolyLine(2.0f, segmentStart, segmentEnd);
                travelled += step;
            }
        }

        private static Vector3 GetEdgeStart(Rect fromRect, WorkflowGraphEdgeKind edgeKind, WorkflowGraphNode from, WorkflowGraphNode to)
        {
            if (edgeKind == WorkflowGraphEdgeKind.Dependency)
            {
                return new Vector3(fromRect.center.x, fromRect.yMax, 0f);
            }

            return new Vector3(fromRect.xMax, fromRect.center.y, 0f);
        }

        private static Vector3 GetEdgeEnd(Rect toRect, WorkflowGraphEdgeKind edgeKind, WorkflowGraphNode from, WorkflowGraphNode to)
        {
            if (edgeKind == WorkflowGraphEdgeKind.Dependency)
            {
                return new Vector3(toRect.center.x, toRect.yMin, 0f);
            }

            return new Vector3(toRect.xMin, toRect.center.y, 0f);
        }

        private static Vector3[] BuildEdgePath(Rect fromRect, Rect toRect, WorkflowGraphEdgeKind edgeKind, Vector3 start, Vector3 end)
        {
            if (edgeKind == WorkflowGraphEdgeKind.Dependency)
            {
                var dependencyLaneY = Mathf.Min(fromRect.yMin, toRect.yMin) - 20f;
                return new[]
                {
                    start,
                    new Vector3(start.x, dependencyLaneY, 0f),
                    new Vector3(end.x, dependencyLaneY, 0f),
                    end
                };
            }

            if (edgeKind == WorkflowGraphEdgeKind.Gate)
            {
                var midX = Mathf.Lerp(start.x, end.x, 0.48f);
                return new[]
                {
                    start,
                    new Vector3(midX, start.y, 0f),
                    new Vector3(midX, end.y, 0f),
                    end
                };
            }

            if (Mathf.Abs(start.y - end.y) <= 2f)
            {
                return new[]
                {
                    start,
                    end
                };
            }

            var horizontalGap = end.x - start.x;
            if (horizontalGap >= 48f)
            {
                var midX = start.x + horizontalGap * 0.5f;
                return new[]
                {
                    start,
                    new Vector3(midX, start.y, 0f),
                    new Vector3(midX, end.y, 0f),
                    end
                };
            }

            var laneOffset = Mathf.Max(28f, Mathf.Abs(end.y - start.y) * 0.35f + 20f);
            var laneY = end.y >= start.y ? start.y + laneOffset : start.y - laneOffset;
            var doglegX = Mathf.Max(start.x + 26f, end.x - 26f);
            return new[]
            {
                start,
                new Vector3(doglegX, start.y, 0f),
                new Vector3(doglegX, laneY, 0f),
                new Vector3(end.x - 16f, laneY, 0f),
                new Vector3(end.x - 16f, end.y, 0f),
                end
            };
        }

        private static Vector3 GetPolylineMidpoint(Vector3[] points)
        {
            if (points == null || points.Length == 0)
            {
                return Vector3.zero;
            }

            if (points.Length == 1)
            {
                return points[0];
            }

            var totalLength = 0f;
            for (var i = 1; i < points.Length; i++)
            {
                totalLength += Vector3.Distance(points[i - 1], points[i]);
            }

            var halfLength = totalLength * 0.5f;
            var currentLength = 0f;
            for (var i = 1; i < points.Length; i++)
            {
                var segmentLength = Vector3.Distance(points[i - 1], points[i]);
                if (currentLength + segmentLength >= halfLength)
                {
                    var t = segmentLength <= Mathf.Epsilon ? 0f : (halfLength - currentLength) / segmentLength;
                    return Vector3.Lerp(points[i - 1], points[i], t);
                }

                currentLength += segmentLength;
            }

            return points[points.Length - 1];
        }

        private static Color GetEdgeColor(WorkflowGraphEdgeKind edgeKind)
        {
            switch (edgeKind)
            {
                case WorkflowGraphEdgeKind.Dependency:
                    return new Color(0.44f, 0.62f, 0.90f, 0.55f);
                case WorkflowGraphEdgeKind.Gate:
                    return new Color(0.38f, 0.73f, 0.96f, 0.70f);
                case WorkflowGraphEdgeKind.OptionalFlow:
                    return OptionalEdgeColor;
                default:
                    return EdgeColor;
            }
        }

        private static void DrawNodeLine(Rect rect, string text, GUIStyle style, Color color)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var previousColor = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previousColor;
        }

        private static Color GetNodeColor(WorkflowGraphNodeState state)
        {
            switch (state)
            {
                case WorkflowGraphNodeState.Ready:
                    return new Color(0.18f, 0.27f, 0.40f, 0.98f);
                case WorkflowGraphNodeState.Running:
                    return new Color(0.12f, 0.34f, 0.35f, 0.98f);
                case WorkflowGraphNodeState.Passed:
                    return new Color(0.15f, 0.33f, 0.20f, 0.98f);
                case WorkflowGraphNodeState.Failed:
                    return new Color(0.38f, 0.17f, 0.17f, 0.98f);
                case WorkflowGraphNodeState.Blocked:
                    return new Color(0.41f, 0.27f, 0.11f, 0.98f);
                case WorkflowGraphNodeState.WaitingExternal:
                    return new Color(0.29f, 0.20f, 0.41f, 0.98f);
                case WorkflowGraphNodeState.Stale:
                    return new Color(0.39f, 0.34f, 0.12f, 0.98f);
                case WorkflowGraphNodeState.Disabled:
                    return new Color(0.22f, 0.23f, 0.25f, 0.98f);
                default:
                    return new Color(0.20f, 0.22f, 0.25f, 0.98f);
            }
        }

        private static Color GetNodeOutlineColor(WorkflowGraphNodeState state)
        {
            switch (state)
            {
                case WorkflowGraphNodeState.Ready:
                    return new Color(0.43f, 0.64f, 0.95f, 0.9f);
                case WorkflowGraphNodeState.Running:
                    return new Color(0.34f, 0.80f, 0.83f, 0.9f);
                case WorkflowGraphNodeState.Passed:
                    return new Color(0.44f, 0.86f, 0.54f, 0.9f);
                case WorkflowGraphNodeState.Failed:
                    return new Color(0.96f, 0.45f, 0.45f, 0.9f);
                case WorkflowGraphNodeState.Blocked:
                    return new Color(0.96f, 0.73f, 0.38f, 0.9f);
                case WorkflowGraphNodeState.WaitingExternal:
                    return new Color(0.78f, 0.58f, 0.98f, 0.9f);
                case WorkflowGraphNodeState.Stale:
                    return new Color(0.92f, 0.83f, 0.34f, 0.9f);
                case WorkflowGraphNodeState.Disabled:
                    return new Color(0.42f, 0.46f, 0.50f, 0.8f);
                default:
                    return new Color(0.52f, 0.56f, 0.62f, 0.85f);
            }
        }
    }
}
