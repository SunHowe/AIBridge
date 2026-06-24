using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIBridge.Editor
{
    internal enum WorkflowGraphNodeKind
    {
        Root,
        Preflight,
        Selector,
        Sequence,
        Parallel,
        Pipeline,
        Condition,
        Action,
        Gate,
        Handoff,
        Artifact
    }

    internal enum WorkflowGraphNodeState
    {
        NotStarted,
        Ready,
        Running,
        Passed,
        Failed,
        Blocked,
        WaitingExternal,
        Stale,
        Disabled
    }

    internal sealed class WorkflowGraphDocument
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string SourcePath { get; set; }
        public string SourceRootPath { get; set; }
        public string Mode { get; set; }
        public string Summary { get; set; }
        public string Warning { get; set; }
        public List<WorkflowGraphNode> Nodes { get; private set; } = new List<WorkflowGraphNode>();
        public List<WorkflowGraphEdge> Edges { get; private set; } = new List<WorkflowGraphEdge>();

        public WorkflowGraphNode FindNode(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (var i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                if (node != null && string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }
            }

            return null;
        }
    }

    internal sealed class WorkflowGraphNode
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Description { get; set; }
        public string SourcePath { get; set; }
        public string StatusText { get; set; }
        public string ParentNodeId { get; set; }
        public string SemanticRole { get; set; }
        public bool IsOptional { get; set; }
        public WorkflowGraphNodeKind Kind { get; set; }
        public WorkflowGraphNodeState State { get; set; }
        public int LayoutColumn { get; set; }
        public int LayoutOrder { get; set; }
        public int LayoutRow { get; set; }
        public Rect Rect { get; set; }
        public List<WorkflowGraphDetail> Details { get; private set; } = new List<WorkflowGraphDetail>();
    }

    internal enum WorkflowGraphEdgeKind
    {
        Flow,
        Dependency,
        Gate,
        OptionalFlow
    }

    internal sealed class WorkflowGraphEdge
    {
        public string FromNodeId { get; set; }
        public string ToNodeId { get; set; }
        public string Label { get; set; }
        public WorkflowGraphEdgeKind Kind { get; set; }
    }

    internal sealed class WorkflowGraphDetail
    {
        public string Label { get; set; }
        public string Value { get; set; }

        public WorkflowGraphDetail(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }
}
