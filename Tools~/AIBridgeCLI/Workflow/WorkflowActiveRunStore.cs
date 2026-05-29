using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace AIBridgeCLI.Workflow
{
    public sealed class WorkflowActiveRunPointer
    {
        [JsonProperty("runId")]
        public string RunId { get; set; }

        [JsonProperty("recipeName")]
        public string RecipeName { get; set; }

        [JsonProperty("runDirectory")]
        public string RunDirectory { get; set; }

        [JsonProperty("attachedAtUtc")]
        public string AttachedAtUtc { get; set; }

        [JsonProperty("updatedAtUtc")]
        public string UpdatedAtUtc { get; set; }
    }

    public static class WorkflowActiveRunStore
    {
        private const string ActiveRunFileName = "active-run.json";

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public static string ActiveRunPath
        {
            get { return Path.Combine(WorkflowPathHelper.GetWorkflowRootDirectory(), ActiveRunFileName); }
        }

        public static WorkflowActiveRunPointer Load()
        {
            if (!File.Exists(ActiveRunPath))
            {
                return null;
            }

            try
            {
                var pointer = JsonConvert.DeserializeObject<WorkflowActiveRunPointer>(File.ReadAllText(ActiveRunPath, Encoding.UTF8));
                if (pointer == null || string.IsNullOrWhiteSpace(pointer.RunId))
                {
                    return null;
                }

                var store = WorkflowRunStore.Open(pointer.RunId);
                return File.Exists(store.ManifestPath) ? pointer : null;
            }
            catch
            {
                return null;
            }
        }

        public static WorkflowActiveRunPointer Save(WorkflowRunManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.RunId))
            {
                throw new ArgumentException("Missing workflow run manifest.");
            }

            var pointer = new WorkflowActiveRunPointer
            {
                RunId = manifest.RunId,
                RecipeName = manifest.RecipeName,
                RunDirectory = WorkflowPathHelper.ToDisplayPath(Path.Combine(WorkflowPathHelper.GetRunsDirectory(), manifest.RunId)),
                AttachedAtUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o")
            };

            var directory = Path.GetDirectoryName(ActiveRunPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(ActiveRunPath, JsonConvert.SerializeObject(pointer, JsonSettings), new UTF8Encoding(false));
            return pointer;
        }

        public static void Clear(string runId = null)
        {
            if (!File.Exists(ActiveRunPath))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(runId))
            {
                var pointer = Load();
                if (pointer == null || !string.Equals(pointer.RunId, runId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            File.Delete(ActiveRunPath);
        }
    }
}
