using System.Collections.Generic;
using UnityEngine;

namespace AIBridge.Editor
{
    internal static class WorkflowGraphLayout
    {
        private const float NodeWidth = 212f;
        private const float NodeHeight = 92f;
        private const float XSpacing = 286f;
        private const float YSpacing = 118f;

        public static Vector2 Layout(WorkflowGraphDocument document)
        {
            if (document == null || document.Nodes.Count == 0)
            {
                return new Vector2(640f, 360f);
            }

            if (document.Mode == "routing")
            {
                return LayoutRouting(document);
            }

            if (document.Mode == "run")
            {
                return LayoutRun(document);
            }

            if (document.Mode == "branch-detail")
            {
                return LayoutBranchDetail(document);
            }

            return LayoutRecipe(document);
        }

        private static Vector2 LayoutRouting(WorkflowGraphDocument document)
        {
            SetRect(document, "root", 36f, 248f);
            SetRect(document, "preflight", 322f, 248f);
            SetRect(document, "selector", 608f, 248f);
            SetRect(document, "requirements", 322f, 88f);

            SetRect(document, "branch-implementation", 894f, 24f);
            SetRect(document, "branch-debug", 894f, 142f);
            SetRect(document, "branch-review", 894f, 260f);
            SetRect(document, "branch-validation", 894f, 378f);
            SetRect(document, "branch-orchestration", 894f, 496f);
            SetRect(document, "handoff", 1180f, 260f);

            LayoutOptionalRoutingNodes(document);
            return new Vector2(1430f, 660f);
        }

        private static Vector2 LayoutRun(WorkflowGraphDocument document)
        {
            SetRect(document, "run", 36f, 194f);
            SetRect(document, "artifacts", 330f, 48f);
            SetRect(document, "gates", 330f, 194f);
            SetRect(document, "external", 330f, 340f);

            var phaseIndex = 0;
            for (var i = 0; i < document.Nodes.Count; i++)
            {
                var node = document.Nodes[i];
                if (node == null || !node.Id.StartsWith("run-phase-"))
                {
                    continue;
                }

                node.Rect = new Rect(624f, 36f + phaseIndex * YSpacing, NodeWidth, NodeHeight);
                phaseIndex++;
            }

            return new Vector2(900f, Mathf.Max(460f, 90f + phaseIndex * YSpacing));
        }

        private static Vector2 LayoutRecipe(WorkflowGraphDocument document)
        {
            SetRect(document, "recipe", 36f, 206f);

            var phaseNodes = document.Nodes.FindAll(node => node != null && node.LayoutColumn == 1);
            phaseNodes.Sort(CompareByLayoutOrder);
            var stepNodes = document.Nodes.FindAll(node => node != null && node.LayoutColumn == 2);
            stepNodes.Sort(CompareByLayoutOrder);
            var gateNodes = document.Nodes.FindAll(node => node != null && node.LayoutColumn == 3);
            gateNodes.Sort(CompareByLayoutOrder);

            var phaseY = 36f;
            var phaseCenters = new Dictionary<string, float>();
            for (var i = 0; i < phaseNodes.Count; i++)
            {
                var phaseNode = phaseNodes[i];
                phaseNode.Rect = new Rect(36f + XSpacing, phaseY, NodeWidth, NodeHeight);
                phaseCenters[phaseNode.Id] = phaseNode.Rect.center.y;
                phaseY += YSpacing;
            }

            var stepYByPhase = new Dictionary<string, float>();
            for (var i = 0; i < stepNodes.Count; i++)
            {
                var stepNode = stepNodes[i];
                var parentPhaseId = stepNode.ParentNodeId;
                float y;
                if (!stepYByPhase.TryGetValue(parentPhaseId, out y))
                {
                    float phaseCenter;
                    if (phaseCenters.TryGetValue(parentPhaseId, out phaseCenter))
                    {
                        y = Mathf.Max(24f, phaseCenter - NodeHeight * 0.5f);
                    }
                    else
                    {
                        y = 36f;
                    }
                }

                stepNode.Rect = new Rect(36f + XSpacing * 2f, y, NodeWidth, NodeHeight);
                stepYByPhase[parentPhaseId] = y + YSpacing;
            }

            var gateY = 36f;
            for (var i = 0; i < gateNodes.Count; i++)
            {
                var gateNode = gateNodes[i];
                var anchorNode = document.FindNode(gateNode.ParentNodeId);
                if (anchorNode != null)
                {
                    gateY = Mathf.Max(gateY, anchorNode.Rect.center.y - NodeHeight * 0.5f);
                }

                gateNode.Rect = new Rect(36f + XSpacing * 3f, gateY, NodeWidth, NodeHeight);
                gateY += YSpacing;
            }

            var maxY = 420f;
            for (var i = 0; i < document.Nodes.Count; i++)
            {
                var node = document.Nodes[i];
                if (node != null)
                {
                    maxY = Mathf.Max(maxY, node.Rect.yMax + 36f);
                }
            }

            return new Vector2(1460f, maxY);
        }

        private static Vector2 LayoutBranchDetail(WorkflowGraphDocument document)
        {
            var maxColumn = 0;
            var maxRow = 0;
            for (var i = 0; i < document.Nodes.Count; i++)
            {
                var node = document.Nodes[i];
                if (node == null)
                {
                    continue;
                }

                maxColumn = Mathf.Max(maxColumn, node.LayoutColumn);
                maxRow = Mathf.Max(maxRow, node.LayoutRow);
            }

            for (var i = 0; i < document.Nodes.Count; i++)
            {
                var node = document.Nodes[i];
                if (node == null)
                {
                    continue;
                }

                var column = Mathf.Max(0, node.LayoutColumn);
                var row = Mathf.Max(0, node.LayoutRow);
                node.Rect = new Rect(
                    36f + column * XSpacing,
                    140f + row * (YSpacing + 28f),
                    NodeWidth,
                    NodeHeight);
            }

            LayoutOptionalNodesBetweenAnchors(document, 150f, 120f);

            return new Vector2(
                120f + (maxColumn + 1) * XSpacing,
                240f + (maxRow + 1) * (YSpacing + 28f));
        }

        private static void SetRect(WorkflowGraphDocument document, string id, float x, float y)
        {
            var node = document.FindNode(id);
            if (node != null)
            {
                node.Rect = new Rect(x, y, NodeWidth, NodeHeight);
            }
        }

        private static void LayoutOptionalRoutingNodes(WorkflowGraphDocument document)
        {
            LayoutOptionalNodesBetweenAnchors(document, 160f, 120f);
        }

        private static void LayoutOptionalNodesBetweenAnchors(WorkflowGraphDocument document, float topOffset, float stackSpacing)
        {
            if (document == null)
            {
                return;
            }

            var groupOffsets = new Dictionary<string, int>();
            for (var i = 0; i < document.Nodes.Count; i++)
            {
                var node = document.Nodes[i];
                if (node == null || !node.IsOptional)
                {
                    continue;
                }

                WorkflowGraphNode leftAnchor;
                WorkflowGraphNode rightAnchor;
                if (!TryGetOptionalRoutingAnchors(document, node, out leftAnchor, out rightAnchor))
                {
                    continue;
                }

                var groupKey = leftAnchor.Id + "->" + rightAnchor.Id;
                var offsetIndex = 0;
                groupOffsets.TryGetValue(groupKey, out offsetIndex);
                groupOffsets[groupKey] = offsetIndex + 1;

                // 可选节点挂到两个主锚点之间，避免被误读成额外平行主分支。
                var centerX = (leftAnchor.Rect.center.x + rightAnchor.Rect.center.x) * 0.5f;
                var topY = Mathf.Min(leftAnchor.Rect.yMin, rightAnchor.Rect.yMin) - topOffset - offsetIndex * stackSpacing;
                node.Rect = new Rect(centerX - NodeWidth * 0.5f, topY, NodeWidth, NodeHeight);
            }
        }

        private static bool TryGetOptionalRoutingAnchors(WorkflowGraphDocument document, WorkflowGraphNode node, out WorkflowGraphNode leftAnchor, out WorkflowGraphNode rightAnchor)
        {
            leftAnchor = null;
            rightAnchor = null;
            if (document == null || node == null)
            {
                return false;
            }

            for (var i = 0; i < document.Edges.Count; i++)
            {
                var edge = document.Edges[i];
                if (edge == null)
                {
                    continue;
                }

                if (string.Equals(edge.ToNodeId, node.Id, System.StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = document.FindNode(edge.FromNodeId);
                    if (candidate != null && candidate != node)
                    {
                        leftAnchor = candidate;
                        break;
                    }
                }
            }

            for (var i = 0; i < document.Edges.Count; i++)
            {
                var edge = document.Edges[i];
                if (edge == null)
                {
                    continue;
                }

                if (string.Equals(edge.FromNodeId, node.Id, System.StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = document.FindNode(edge.ToNodeId);
                    if (candidate != null && candidate != node)
                    {
                        rightAnchor = candidate;
                        break;
                    }
                }
            }

            return leftAnchor != null && rightAnchor != null;
        }

        private static int CompareByLayoutOrder(WorkflowGraphNode left, WorkflowGraphNode right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            return left.LayoutOrder.CompareTo(right.LayoutOrder);
        }
    }
}
