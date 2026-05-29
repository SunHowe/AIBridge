using System;
using System.Collections.Generic;

namespace AIBridgeCLI.Workflow
{
    public sealed class WorkflowRunContext
    {
        public const string EnvironmentRunId = "AIBRIDGE_WORKFLOW_RUN_ID";
        public const string WorkflowRunOption = "workflow-run";

        public string RunId { get; set; }
        public string Source { get; set; }
        public bool Explicit { get; set; }

        public static WorkflowRunContext Resolve(Dictionary<string, string> options)
        {
            string runId;
            if (options != null
                && options.TryGetValue(WorkflowRunOption, out runId)
                && !string.IsNullOrWhiteSpace(runId))
            {
                return new WorkflowRunContext
                {
                    RunId = runId.Trim(),
                    Source = "--" + WorkflowRunOption,
                    Explicit = true
                };
            }

            runId = Environment.GetEnvironmentVariable(EnvironmentRunId);
            if (!string.IsNullOrWhiteSpace(runId))
            {
                return new WorkflowRunContext
                {
                    RunId = runId.Trim(),
                    Source = EnvironmentRunId,
                    Explicit = true
                };
            }

            var active = WorkflowActiveRunStore.Load();
            if (active != null && !string.IsNullOrWhiteSpace(active.RunId))
            {
                return new WorkflowRunContext
                {
                    RunId = active.RunId,
                    Source = "active-run",
                    Explicit = false
                };
            }

            return null;
        }
    }
}
