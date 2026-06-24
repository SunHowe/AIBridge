using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AIBridgeCLI.Workflow
{
    public static class WorkflowReportWriter
    {
        public static string WriteMarkdown(WorkflowRunManifest manifest)
        {
            var insight = WorkflowRunInsight.Analyze(manifest);
            manifest.Summary = insight.Summary;
            manifest.TerminalState = insight.TerminalState;
            manifest.TerminalReason = insight.TerminalReason;

            var sb = new StringBuilder();
            sb.AppendLine("# Workflow Report: " + manifest.RunId);
            sb.AppendLine();
            sb.AppendLine("- Recipe: `" + manifest.RecipeName + "`");
            sb.AppendLine("- Status: `" + manifest.Status + "`");
            if (!string.IsNullOrWhiteSpace(manifest.TerminalState))
            {
                sb.AppendLine("- Terminal state: `" + manifest.TerminalState + "`");
            }

            if (!string.IsNullOrWhiteSpace(manifest.TerminalReason))
            {
                sb.AppendLine("- Terminal reason: " + manifest.TerminalReason);
            }

            sb.AppendLine("- Started: `" + manifest.StartedAtUtc + "`");
            if (!string.IsNullOrWhiteSpace(manifest.EndedAtUtc))
            {
                sb.AppendLine("- Ended: `" + manifest.EndedAtUtc + "`");
            }

            sb.AppendLine("- Run directory: `" + WorkflowPathHelper.ToDisplayPath(System.IO.Path.Combine(WorkflowPathHelper.GetRunsDirectory(), manifest.RunId)) + "`");
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine("| CLI commands | " + manifest.Summary.CliCommandCount + " |");
            sb.AppendLine("| Agent/manual steps | " + manifest.Summary.AgentStepCount + " |");
            sb.AppendLine("| Iteration count | " + manifest.Summary.IterationCount + " |");
            sb.AppendLine("| External skipped | " + manifest.Summary.ExternalSkippedCount + " |");
            sb.AppendLine("| Missing external imports | " + manifest.Summary.MissingExternalImportCount + " |");
            sb.AppendLine("| Artifacts | " + manifest.Summary.ArtifactCount + " |");
            sb.AppendLine("| Failed gates | " + manifest.Summary.FailedGateCount + " |");
            sb.AppendLine("| Fresh evidence | " + manifest.Summary.FreshEvidenceCount + " |");
            sb.AppendLine("| Stale evidence | " + manifest.Summary.StaleEvidenceCount + " |");
            sb.AppendLine("| Missing evidence | " + manifest.Summary.MissingEvidenceCount + " |");
            sb.AppendLine("| Unknown evidence | " + manifest.Summary.UnknownEvidenceCount + " |");
            sb.AppendLine("| Imported verdicts | " + manifest.Summary.ImportedVerdictCount + " |");
            sb.AppendLine("| Confirmed verdicts | " + manifest.Summary.ConfirmedVerdictCount + " |");
            sb.AppendLine("| Refuted verdicts | " + manifest.Summary.RefutedVerdictCount + " |");
            sb.AppendLine("| Uncertain verdicts | " + manifest.Summary.UncertainVerdictCount + " |");
            sb.AppendLine("| Open risks | " + manifest.Summary.OpenRiskCount + " |");
            sb.AppendLine();

            WriteSkillScopeSection(sb, manifest);

            sb.AppendLine("## Phases");
            sb.AppendLine();
            sb.AppendLine("| Phase | Status | Steps | Artifacts | Error |");
            sb.AppendLine("|---|---|---:|---:|---|");
            foreach (var phase in manifest.PhaseStates ?? new List<WorkflowPhaseState>())
            {
                sb.AppendLine("| `" + phase.PhaseId + "` | `" + phase.Status + "` | " + phase.StepIds.Count + " | " + phase.ArtifactIds.Count + " | " + EscapeTable(phase.Error) + " |");
            }

            sb.AppendLine();
            sb.AppendLine("## Steps");
            sb.AppendLine();
            sb.AppendLine("| Step | Kind | Status | Outputs | Missing Outputs | Command | Error |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var step in manifest.StepStates ?? new List<WorkflowStepState>())
            {
                sb.AppendLine("| `" + step.StepId + "` | `" + step.Kind + "` | `" + step.Status + "` | " + EscapeTable(FormatList(step.Outputs)) + " | " + EscapeTable(FormatList(FindMissingOutputs(step.StepId, insight.ExternalImportGaps))) + " | " + EscapeTable(step.Command) + " | " + EscapeTable(step.Error) + " |");
            }

            sb.AppendLine();
            sb.AppendLine("## Gates");
            sb.AppendLine();
            sb.AppendLine("| Gate | Kind | Required | Status | Message |");
            sb.AppendLine("|---|---|---:|---|---|");
            foreach (var gate in manifest.GateResults)
            {
                sb.AppendLine("| `" + gate.GateId + "` | `" + gate.Kind + "` | " + (gate.Required ? "yes" : "no") + " | `" + gate.Status + "` | " + EscapeTable(gate.Message) + " |");
            }

            sb.AppendLine();
            sb.AppendLine("## Artifacts");
            sb.AppendLine();
            sb.AppendLine("| Artifact | Kind | Path | Source Command | Summary |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var artifact in manifest.ArtifactRefs)
            {
                var kind = artifact.Kind;
                if (!string.IsNullOrWhiteSpace(artifact.SemanticKind))
                {
                    kind += "/" + artifact.SemanticKind;
                }

                if (!string.IsNullOrWhiteSpace(artifact.Schema))
                {
                    kind += "/" + artifact.Schema;
                }

                sb.AppendLine("| `" + artifact.ArtifactId + "` | `" + kind + "` | `" + artifact.Path + "` | " + EscapeTable(artifact.SourceCommand) + " | " + EscapeTable(artifact.Summary) + " |");
            }

            WritePerformanceEvidenceSection(sb, manifest);
            WriteEvidenceFreshnessSection(sb, insight.EvidenceFreshness);
            WriteExternalImportGapSection(sb, insight.ExternalImportGaps);
            WriteVerdictSection(sb, manifest);

            sb.AppendLine();
            sb.AppendLine("## Reproduce");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine("AIBridgeCLI workflow status --run " + manifest.RunId);
            sb.AppendLine("AIBridgeCLI workflow report --run " + manifest.RunId + " --format markdown");
            sb.AppendLine("```");
            return sb.ToString();
        }

        private static void WriteSkillScopeSection(StringBuilder sb, WorkflowRunManifest manifest)
        {
            if (!HasSkillScopes(manifest))
            {
                return;
            }

            sb.AppendLine("## Skill Scope");
            sb.AppendLine();
            sb.AppendLine("| Scope | Required Skills | Release After |");
            sb.AppendLine("|---|---|---|");
            foreach (var phase in manifest.PhaseStates ?? new List<WorkflowPhaseState>())
            {
                if (!HasSkillScope(phase.RequiredSkills, phase.ReleaseSkillsAfter))
                {
                    continue;
                }

                sb.AppendLine("| `phase:" + phase.PhaseId + "` | " + FormatSkillList(phase.RequiredSkills) + " | " + FormatSkillList(phase.ReleaseSkillsAfter) + " |");
            }

            foreach (var step in manifest.StepStates ?? new List<WorkflowStepState>())
            {
                if (!HasSkillScope(step.RequiredSkills, step.ReleaseSkillsAfter))
                {
                    continue;
                }

                sb.AppendLine("| `step:" + step.StepId + "` | " + FormatSkillList(step.RequiredSkills) + " | " + FormatSkillList(step.ReleaseSkillsAfter) + " |");
            }

            sb.AppendLine();
        }

        private static bool HasSkillScopes(WorkflowRunManifest manifest)
        {
            if (manifest == null)
            {
                return false;
            }

            foreach (var phase in manifest.PhaseStates ?? new List<WorkflowPhaseState>())
            {
                if (HasSkillScope(phase.RequiredSkills, phase.ReleaseSkillsAfter))
                {
                    return true;
                }
            }

            foreach (var step in manifest.StepStates ?? new List<WorkflowStepState>())
            {
                if (HasSkillScope(step.RequiredSkills, step.ReleaseSkillsAfter))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSkillScope(List<string> requiredSkills, List<string> releaseSkillsAfter)
        {
            return requiredSkills != null && requiredSkills.Count > 0
                || releaseSkillsAfter != null && releaseSkillsAfter.Count > 0;
        }

        private static string FormatSkillList(List<string> skills)
        {
            if (skills == null || skills.Count == 0)
            {
                return "";
            }

            return "`" + string.Join("`, `", skills.ToArray()) + "`";
        }

        private static string FormatList(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "";
            }

            return string.Join(", ", values.ToArray());
        }

        private static void WritePerformanceEvidenceSection(StringBuilder sb, WorkflowRunManifest manifest)
        {
            var runtimeRows = ReadRuntimePerfRows(manifest);
            var profilerRows = ReadProfilerSnapshotRows(manifest);
            if (runtimeRows.Count == 0 && profilerRows.Count == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine("## Performance Evidence");
            sb.AppendLine();

            if (runtimeRows.Count > 0)
            {
                sb.AppendLine("### Runtime Perf");
                sb.AppendLine();
                sb.AppendLine("| Artifact | Status | Target | Window | FPS | Frame Time | Hitches | Memory | GC | Rendering | Recorder | Warnings | Unsupported |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
                foreach (var row in runtimeRows)
                {
                    sb.AppendLine("| `" + row.ArtifactId + "` | `" + EscapeTable(row.Status) + "` | " + EscapeTable(row.Target) + " | " + EscapeTable(row.Window) + " | " + EscapeTable(row.Fps) + " | " + EscapeTable(row.FrameTime) + " | " + EscapeTable(row.Hitches) + " | " + EscapeTable(row.Memory) + " | " + EscapeTable(row.Gc) + " | " + EscapeTable(row.Rendering) + " | " + EscapeTable(row.Recorder) + " | " + EscapeTable(row.Warnings) + " | " + EscapeTable(row.Unsupported) + " |");
                }

                sb.AppendLine();
            }

            if (profilerRows.Count > 0)
            {
                sb.AppendLine("### Editor Profiler");
                sb.AppendLine();
                sb.AppendLine("| Artifact | Scope | Status | Frame | Memory | Rendering | Script / GC | Warnings | Unsupported |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
                foreach (var row in profilerRows)
                {
                    sb.AppendLine("| `" + row.ArtifactId + "` | " + EscapeTable(row.Scope) + " | " + EscapeTable(row.Status) + " | " + EscapeTable(row.Frame) + " | " + EscapeTable(row.Memory) + " | " + EscapeTable(row.Rendering) + " | " + EscapeTable(row.Script) + " | " + EscapeTable(row.Warnings) + " | " + EscapeTable(row.Unsupported) + " |");
                }

                sb.AppendLine();
            }
        }

        private static List<RuntimePerfRow> ReadRuntimePerfRows(WorkflowRunManifest manifest)
        {
            var rows = new List<RuntimePerfRow>();
            if (manifest == null || manifest.ArtifactRefs == null)
            {
                return rows;
            }

            foreach (var artifact in manifest.ArtifactRefs)
            {
                if (!ArtifactMatchesKind(artifact, "runtime-perf"))
                {
                    continue;
                }

                foreach (var obj in ReadArtifactObjects(artifact.Path))
                {
                    var payload = FindObjectWithFields(obj, "fps", "frameTimeMs");
                    if (payload != null)
                    {
                        rows.Add(BuildRuntimePerfRow(artifact, payload, obj));
                    }
                    else
                    {
                        var row = BuildRuntimePerfFailureRow(artifact, obj);
                        if (row != null)
                        {
                            rows.Add(row);
                        }
                    }
                }
            }

            return rows;
        }

        private static List<ProfilerSnapshotRow> ReadProfilerSnapshotRows(WorkflowRunManifest manifest)
        {
            var rows = new List<ProfilerSnapshotRow>();
            if (manifest == null || manifest.ArtifactRefs == null)
            {
                return rows;
            }

            foreach (var artifact in manifest.ArtifactRefs)
            {
                if (!ArtifactMatchesKind(artifact, "profiler-snapshot"))
                {
                    continue;
                }

                foreach (var obj in ReadArtifactObjects(artifact.Path))
                {
                    var payload = FindObjectWithFields(obj, "stats");
                    if (payload != null)
                    {
                        rows.Add(BuildProfilerSnapshotRow(artifact, payload));
                    }
                }
            }

            return rows;
        }

        private static RuntimePerfRow BuildRuntimePerfRow(WorkflowArtifactRef artifact, JObject payload, JObject commandResult)
        {
            var row = new RuntimePerfRow
            {
                ArtifactId = artifact.ArtifactId,
                Status = FormatCommandStatus(commandResult),
                Target = ReadString(payload, "targetId"),
                Window = FormatRuntimeWindow(payload),
                Fps = FormatFps(ReadToken(payload, "fps")),
                FrameTime = FormatFrameTime(ReadToken(payload, "frameTimeMs")),
                Hitches = FormatHitches(ReadToken(payload, "frameTimeMs")),
                Memory = FormatRuntimeMemory(ReadToken(payload, "memory")),
                Gc = FormatRuntimeGc(ReadToken(payload, "gc")),
                Rendering = FormatRendering(ReadToken(payload, "rendering")),
                Recorder = ReadString(payload, "recorderMode"),
                Warnings = FormatWarnings(ReadToken(payload, "warnings")),
                Unsupported = FormatUnsupported(ReadToken(payload, "unsupported"))
            };

            return row;
        }

        private static RuntimePerfRow BuildRuntimePerfFailureRow(WorkflowArtifactRef artifact, JObject commandResult)
        {
            var status = FormatCommandStatus(commandResult);
            var error = ReadString(commandResult, "error");
            if (string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(error))
            {
                return null;
            }

            return new RuntimePerfRow
            {
                ArtifactId = artifact.ArtifactId,
                Status = status,
                Target = ReadString(commandResult, "data", "data", "targetId"),
                Warnings = error
            };
        }

        private static ProfilerSnapshotRow BuildProfilerSnapshotRow(WorkflowArtifactRef artifact, JObject payload)
        {
            var stats = ReadToken(payload, "stats");
            return new ProfilerSnapshotRow
            {
                ArtifactId = artifact.ArtifactId,
                Scope = ExtractProfilerScope(artifact.SourceCommand),
                Status = FormatProfilerStatus(ReadToken(stats, "status")),
                Frame = FormatProfilerFrame(ReadToken(stats, "frame")),
                Memory = FormatProfilerMemory(ReadToken(stats, "memory")),
                Rendering = FormatRendering(ReadToken(stats, "rendering")),
                Script = FormatProfilerScript(ReadToken(stats, "script")),
                Warnings = FormatWarnings(ReadToken(payload, "warnings")),
                Unsupported = FormatUnsupported(ReadToken(payload, "unsupported"))
            };
        }

        private static string FormatRuntimeWindow(JObject payload)
        {
            var parts = new List<string>();
            AddMetric(parts, "duration", FormatMilliseconds(ReadDouble(payload, "durationMs")));
            AddMetric(parts, "interval", FormatMilliseconds(ReadDouble(payload, "intervalMs")));
            AddMetric(parts, "samples", FormatInteger(ReadDouble(payload, "sampleCount")));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatFps(JToken fps)
        {
            var parts = new List<string>();
            AddMetric(parts, "avg", FormatDouble(ReadDouble(fps, "avg")));
            AddMetric(parts, "min", FormatDouble(ReadDouble(fps, "min")));
            AddMetric(parts, "max", FormatDouble(ReadDouble(fps, "max")));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatFrameTime(JToken frame)
        {
            var parts = new List<string>();
            AddMetric(parts, "avg", FormatMilliseconds(ReadDouble(frame, "avg")));
            AddMetric(parts, "p95", FormatMilliseconds(ReadDouble(frame, "p95")));
            AddMetric(parts, "p99", FormatMilliseconds(ReadDouble(frame, "p99")));
            AddMetric(parts, "max", FormatMilliseconds(ReadDouble(frame, "max")));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatHitches(JToken frame)
        {
            var count = FormatInteger(ReadDouble(frame, "hitchCount"));
            var threshold = FormatMilliseconds(ReadDouble(frame, "hitchThresholdMs"));
            if (string.IsNullOrWhiteSpace(count) && string.IsNullOrWhiteSpace(threshold))
            {
                return "";
            }

            return string.IsNullOrWhiteSpace(threshold) ? count : count + " >= " + threshold;
        }

        private static string FormatRuntimeMemory(JToken memory)
        {
            var parts = new List<string>();
            AddMetric(parts, "mono", FormatBytes(ReadLong(memory, "monoUsedBytes")));
            AddMetric(parts, "gc", FormatBytes(ReadLong(memory, "gcUsedBytes")));
            AddMetric(parts, "reserved", FormatBytes(ReadLong(memory, "totalReservedBytes")));
            AddMetric(parts, "system", FormatBytes(ReadLong(memory, "systemUsedBytes")));
            AddMetric(parts, "gfx", FormatBytes(ReadLong(memory, "graphicsDriverBytes")));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatRuntimeGc(JToken gc)
        {
            var parts = new List<string>();
            AddMetric(parts, "alloc", FormatBytes(ReadLong(gc, "allocatedBytesDelta")));
            AddMetric(parts, "gc0", FormatInteger(ReadDouble(gc, "collectionCount0Delta")));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatProfilerStatus(JToken status)
        {
            var parts = new List<string>();
            AddMetric(parts, "enabled", FormatBool(ReadBool(status, "profilerEnabled")));
            AddMetric(parts, "playing", FormatBool(ReadBool(status, "isPlaying")));
            AddMetric(parts, "paused", FormatBool(ReadBool(status, "isPaused")));
            AddMetric(parts, "editor", FormatBool(ReadBool(status, "supportsEditorProfiler")));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatProfilerFrame(JToken frame)
        {
            var parts = new List<string>();
            AddMetric(parts, "frame", FormatInteger(ReadDouble(frame, "frameCount")));
            AddMetric(parts, "fps", FormatDouble(ReadDouble(frame, "fps")));
            AddMetric(parts, "delta", FormatMilliseconds(ReadDouble(frame, "deltaTimeMs")));
            AddMetric(parts, "source", ReadString(frame, "sampleSource"));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatProfilerMemory(JToken memory)
        {
            var parts = new List<string>();
            AddMetric(parts, "reserved", FormatBytes(ReadLong(memory, "totalReservedBytes")));
            AddMetric(parts, "allocated", FormatBytes(ReadLong(memory, "totalAllocatedBytes")));
            AddMetric(parts, "mono", FormatBytes(ReadLong(memory, "monoUsedBytes")));
            AddMetric(parts, "gc", FormatBytes(ReadLong(memory, "gcUsedBytes")));
            AddMetric(parts, "gfx", FormatBytes(ReadLong(memory, "graphicsDriverBytes")));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatRendering(JToken rendering)
        {
            var parts = new List<string>();
            var width = ReadLong(rendering, "screenWidth");
            var height = ReadLong(rendering, "screenHeight");
            if (width.HasValue && height.HasValue)
            {
                AddMetric(parts, "screen", width.Value.ToString(CultureInfo.InvariantCulture) + "x" + height.Value.ToString(CultureInfo.InvariantCulture));
            }

            AddMetric(parts, "device", ReadString(rendering, "graphicsDeviceType"));
            AddMetric(parts, "target", FormatInteger(ReadDouble(rendering, "targetFrameRate")));
            AddMetric(parts, "vSync", FormatInteger(ReadDouble(rendering, "vSyncCount")));
            AddMetric(parts, "pipeline", ReadString(rendering, "renderPipeline"));
            return string.Join(", ", parts.ToArray());
        }

        private static string FormatProfilerScript(JToken script)
        {
            var parts = new List<string>();
            AddMetric(parts, "main", FormatMilliseconds(ReadDouble(script, "mainThreadFrameTimeMs")));
            AddMetric(parts, "gc alloc", FormatBytes(ReadLong(script, "gcAllocatedBytesDelta")));
            AddMetric(parts, "gc0", FormatInteger(ReadDouble(script, "gcCollectionCount0Delta")));
            AddMetric(parts, "source", ReadString(script, "timingSource"));
            return string.Join(", ", parts.ToArray());
        }

        private static string ExtractProfilerScope(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return "";
            }

            var normalized = command.Trim();
            if (!normalized.StartsWith("profiler ", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return normalized.Substring("profiler ".Length).Trim();
        }

        private static string FormatWarnings(JToken token)
        {
            return TrimCell(FormatArrayValues(token, "warning"));
        }

        private static string FormatUnsupported(JToken token)
        {
            var array = token as JArray;
            if (array == null || array.Count == 0)
            {
                return "";
            }

            var parts = new List<string>();
            for (var i = 0; i < array.Count && i < 3; i++)
            {
                var item = array[i];
                var feature = ReadString(item, "feature");
                var reason = ReadString(item, "reason");
                if (string.IsNullOrWhiteSpace(feature))
                {
                    parts.Add(TrimCell(item.ToString()));
                }
                else if (string.IsNullOrWhiteSpace(reason))
                {
                    parts.Add(feature);
                }
                else
                {
                    parts.Add(feature + ": " + reason);
                }
            }

            if (array.Count > 3)
            {
                parts.Add("+" + (array.Count - 3).ToString(CultureInfo.InvariantCulture) + " more");
            }

            return TrimCell(string.Join("; ", parts.ToArray()));
        }

        private static string FormatArrayValues(JToken token, string fallbackLabel)
        {
            var array = token as JArray;
            if (array == null || array.Count == 0)
            {
                return "";
            }

            var parts = new List<string>();
            for (var i = 0; i < array.Count && i < 3; i++)
            {
                parts.Add(array[i].Type == JTokenType.String ? array[i].Value<string>() : array[i].ToString());
            }

            if (array.Count > 3)
            {
                parts.Add("+" + (array.Count - 3).ToString(CultureInfo.InvariantCulture) + " more " + fallbackLabel + "s");
            }

            return string.Join("; ", parts.ToArray());
        }

        private static JObject FindObjectWithFields(JToken token, params string[] fields)
        {
            var obj = token as JObject;
            if (obj != null)
            {
                var hasAll = true;
                foreach (var field in fields)
                {
                    if (!HasProperty(obj, field))
                    {
                        hasAll = false;
                        break;
                    }
                }

                if (hasAll)
                {
                    return obj;
                }

                foreach (var property in obj.Properties())
                {
                    var found = FindObjectWithFields(property.Value, fields);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            var array = token as JArray;
            if (array != null)
            {
                foreach (var item in array)
                {
                    var found = FindObjectWithFields(item, fields);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static bool HasProperty(JObject obj, string name)
        {
            JToken ignored;
            return obj != null && obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out ignored);
        }

        private static JToken ReadToken(JToken token, params string[] path)
        {
            var current = token;
            foreach (var part in path)
            {
                var obj = current as JObject;
                if (obj == null)
                {
                    return null;
                }

                if (!obj.TryGetValue(part, StringComparison.OrdinalIgnoreCase, out current))
                {
                    return null;
                }
            }

            return current;
        }

        private static string ReadString(JToken token, params string[] path)
        {
            var value = ReadToken(token, path);
            if (value == null || value.Type == JTokenType.Null)
            {
                return "";
            }

            return value.Type == JTokenType.String ? value.Value<string>() : value.ToString();
        }

        private static double? ReadDouble(JToken token, params string[] path)
        {
            var value = ReadToken(token, path);
            if (value == null || value.Type == JTokenType.Null)
            {
                return null;
            }

            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                return value.Value<double>();
            }

            double parsed;
            return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : (double?)null;
        }

        private static long? ReadLong(JToken token, params string[] path)
        {
            var value = ReadToken(token, path);
            if (value == null || value.Type == JTokenType.Null)
            {
                return null;
            }

            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                return value.Value<long>();
            }

            long parsed;
            return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : (long?)null;
        }

        private static bool? ReadBool(JToken token, params string[] path)
        {
            var value = ReadToken(token, path);
            if (value == null || value.Type == JTokenType.Null)
            {
                return null;
            }

            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>();
            }

            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) ? parsed : (bool?)null;
        }

        private static void AddMetric(List<string> parts, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(label + " " + value);
            }
        }

        private static string FormatMilliseconds(double? value)
        {
            return value.HasValue ? FormatDouble(value) + " ms" : "";
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";
        }

        private static string FormatInteger(double? value)
        {
            return value.HasValue ? ((long)Math.Round(value.Value)).ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string FormatBytes(long? bytes)
        {
            if (!bytes.HasValue)
            {
                return "";
            }

            return (bytes.Value / 1048576d).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
        }

        private static string FormatBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "true" : "false") : "";
        }

        private static string FormatCommandStatus(JObject commandResult)
        {
            var success = ReadBool(commandResult, "success");
            if (!success.HasValue)
            {
                return "";
            }

            return success.Value ? "passed" : "failed";
        }

        private static bool ArtifactMatchesKind(WorkflowArtifactRef artifact, string kind)
        {
            return artifact != null
                && (string.Equals(artifact.Kind, kind, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(artifact.SemanticKind, kind, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(artifact.Schema, kind, StringComparison.OrdinalIgnoreCase));
        }

        private static string TrimCell(string value)
        {
            const int maxLength = 220;
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private static List<string> FindMissingOutputs(string stepId, List<WorkflowExternalImportGap> gaps)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(stepId) || gaps == null)
            {
                return result;
            }

            foreach (var gap in gaps)
            {
                if (gap != null && string.Equals(gap.StepId, stepId, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddRange(gap.MissingOutputs);
                }
            }

            return result;
        }

        private static void WriteEvidenceFreshnessSection(StringBuilder sb, List<WorkflowEvidenceFreshnessEntry> entries)
        {
            sb.AppendLine();
            sb.AppendLine("## Evidence Freshness");
            sb.AppendLine();
            if (entries == null || entries.Count == 0)
            {
                sb.AppendLine("- No evidence freshness entries.");
                return;
            }

            sb.AppendLine("| Type | Ref | Kind | Freshness | Age | Threshold | Reason |");
            sb.AppendLine("|---|---|---|---|---:|---:|---|");
            foreach (var entry in entries)
            {
                sb.AppendLine("| `" + EscapeTable(entry.RefType) + "` | `" + EscapeTable(entry.RefId) + "` | `" + EscapeTable(entry.Kind ?? entry.Schema) + "` | `" + EscapeTable(entry.Freshness) + "` | " + FormatMinutes(entry.AgeMinutes) + " | " + FormatMinutes(entry.ThresholdMinutes) + " | " + EscapeTable(entry.Reason) + " |");
            }
        }

        private static void WriteExternalImportGapSection(StringBuilder sb, List<WorkflowExternalImportGap> gaps)
        {
            sb.AppendLine();
            sb.AppendLine("## External Handoff");
            sb.AppendLine();
            if (gaps == null || gaps.Count == 0)
            {
                sb.AppendLine("- All required external outputs have been imported.");
                return;
            }

            sb.AppendLine("| Step | Phase | Kind | Missing Outputs | Imported Artifacts | Reason |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var gap in gaps)
            {
                sb.AppendLine("| `" + EscapeTable(gap.StepId) + "` | `" + EscapeTable(gap.PhaseId) + "` | `" + EscapeTable(gap.Kind) + "` | " + EscapeTable(FormatList(gap.MissingOutputs)) + " | " + EscapeTable(FormatList(gap.ImportedArtifactIds)) + " | " + EscapeTable(gap.Reason) + " |");
            }
        }

        private static string FormatMinutes(double? minutes)
        {
            if (!minutes.HasValue)
            {
                return "";
            }

            return minutes.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "m";
        }

        private static void WriteVerdictSection(StringBuilder sb, WorkflowRunManifest manifest)
        {
            var verdicts = ReadVerdicts(manifest);
            if (verdicts.Count == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine("## Imported Verdicts");
            WriteVerdictTable(sb, "confirmed", verdicts);
            WriteVerdictTable(sb, "refuted", verdicts);
            WriteVerdictTable(sb, "uncertain", verdicts);
        }

        private static void WriteVerdictTable(StringBuilder sb, string status, List<VerdictRow> verdicts)
        {
            var wroteHeader = false;
            foreach (var verdict in verdicts)
            {
                if (!string.Equals(verdict.Status, status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!wroteHeader)
                {
                    sb.AppendLine();
                    sb.AppendLine("### " + status);
                    sb.AppendLine();
                    sb.AppendLine("| Claim | Artifact | Evidence | Reason | Remaining Risk |");
                    sb.AppendLine("|---|---|---|---|---|");
                    wroteHeader = true;
                }

                sb.AppendLine("| " + EscapeTable(verdict.ClaimId) + " | `" + verdict.ArtifactId + "` | " + EscapeTable(verdict.EvidenceRefs) + " | " + EscapeTable(verdict.Reason) + " | " + EscapeTable(verdict.RemainingRisk) + " |");
            }
        }

        private static List<VerdictRow> ReadVerdicts(WorkflowRunManifest manifest)
        {
            var result = new List<VerdictRow>();
            if (manifest == null || manifest.ArtifactRefs == null)
            {
                return result;
            }

            foreach (var artifact in manifest.ArtifactRefs)
            {
                if (!string.Equals(artifact.Kind, "verdict", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(artifact.Schema, "Verdict", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var obj in ReadArtifactObjects(artifact.Path))
                {
                    result.Add(new VerdictRow
                    {
                        ArtifactId = artifact.ArtifactId,
                        ClaimId = (string)obj["claimId"],
                        Status = (string)obj["status"],
                        Reason = (string)obj["reason"],
                        RemainingRisk = (string)obj["remainingRisk"],
                        EvidenceRefs = ReadEvidenceRefs(obj["evidenceRefs"] ?? obj["evidence"])
                    });
                }
            }

            return result;
        }

        private static IEnumerable<JObject> ReadArtifactObjects(string displayPath)
        {
            var path = ResolveArtifactPath(displayPath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                yield break;
            }

            JToken payload;
            try
            {
                payload = JToken.Parse(File.ReadAllText(path));
            }
            catch
            {
                yield break;
            }

            var obj = payload as JObject;
            if (obj != null)
            {
                yield return obj;
                yield break;
            }

            var array = payload as JArray;
            if (array == null)
            {
                yield break;
            }

            foreach (var item in array)
            {
                obj = item as JObject;
                if (obj != null)
                {
                    yield return obj;
                }
            }
        }

        private static string ReadEvidenceRefs(JToken token)
        {
            if (token == null)
            {
                return "";
            }

            var array = token as JArray;
            if (array == null)
            {
                return token.ToString();
            }

            var values = new List<string>();
            foreach (var item in array)
            {
                values.Add(item.ToString());
            }

            return string.Join(", ", values.ToArray());
        }

        private static string ResolveArtifactPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(WorkflowPathHelper.GetProjectRoot(), path));
        }

        private static string EscapeTable(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private sealed class VerdictRow
        {
            public string ArtifactId { get; set; }
            public string ClaimId { get; set; }
            public string Status { get; set; }
            public string EvidenceRefs { get; set; }
            public string Reason { get; set; }
            public string RemainingRisk { get; set; }
        }

        private sealed class RuntimePerfRow
        {
            public string ArtifactId { get; set; }
            public string Status { get; set; }
            public string Target { get; set; }
            public string Window { get; set; }
            public string Fps { get; set; }
            public string FrameTime { get; set; }
            public string Hitches { get; set; }
            public string Memory { get; set; }
            public string Gc { get; set; }
            public string Rendering { get; set; }
            public string Recorder { get; set; }
            public string Warnings { get; set; }
            public string Unsupported { get; set; }
        }

        private sealed class ProfilerSnapshotRow
        {
            public string ArtifactId { get; set; }
            public string Scope { get; set; }
            public string Status { get; set; }
            public string Frame { get; set; }
            public string Memory { get; set; }
            public string Rendering { get; set; }
            public string Script { get; set; }
            public string Warnings { get; set; }
            public string Unsupported { get; set; }
        }
    }
}
