using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AIBridge.Internal.Json;
using UnityEngine;

namespace AIBridge.Editor.ScriptExecution
{
    internal enum ScriptFileKind
    {
        Batch,
        CSharp
    }

    internal sealed class ScriptFileEntry
    {
        public string Path { get; private set; }
        public ScriptFileKind Kind { get; private set; }

        public ScriptFileEntry(string path, ScriptFileKind kind)
        {
            Path = path;
            Kind = kind;
        }
    }

    internal sealed class CodeScriptExecutionViewState
    {
        public string RequestId;
        public string ScriptPath;
        public bool IsRunning;
        public CommandResult LastResult;
        public string Message;
    }

    internal static class ScriptFileUtility
    {
        public const int DefaultCodeTimeoutMs = 5000;
        public const string CodeExecutionCacheDirectory = ".aibridge/code";

        public static List<ScriptFileEntry> FindScripts(string scriptDirectory)
        {
            var entries = new List<ScriptFileEntry>();
            if (!string.IsNullOrWhiteSpace(scriptDirectory) && Directory.Exists(scriptDirectory))
            {
                entries.AddRange(Directory
                    .GetFiles(scriptDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(IsSupportedScriptPath)
                    .Select(path => new ScriptFileEntry(path, IsCSharpScriptPath(path) ? ScriptFileKind.CSharp : ScriptFileKind.Batch)));
            }

            return entries
                .OrderBy(entry => entry.Kind)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static CommandRequest BuildCodeExecuteRequest(string scriptPath, int timeoutMs)
        {
            var executionFile = PrepareCodeExecutionFile(scriptPath);
            return new CommandRequest
            {
                id = "scripts-tab-code-" + Guid.NewGuid().ToString("N"),
                type = "code",
                @params = new Dictionary<string, object>
                {
                    { "action", "execute" },
                    { "file", executionFile },
                    { "timeout", timeoutMs <= 0 ? DefaultCodeTimeoutMs : timeoutMs }
                }
            };
        }

        public static CommandRequest BuildCodeStatusRequest()
        {
            return new CommandRequest
            {
                id = "scripts-tab-code-status-" + Guid.NewGuid().ToString("N"),
                type = "code",
                @params = new Dictionary<string, object>
                {
                    { "action", "status" }
                }
            };
        }

        public static CommandRequest BuildCodeCancelRequest(string requestId)
        {
            var parameters = new Dictionary<string, object>
            {
                { "action", "cancel" }
            };
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                parameters["requestId"] = requestId;
            }

            return new CommandRequest
            {
                id = "scripts-tab-code-cancel-" + Guid.NewGuid().ToString("N"),
                type = "code",
                @params = parameters
            };
        }

        public static CommandResult ReadCodeResultFile(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return null;
            }

            var path = Path.Combine(AIBridge.BridgeDirectory, "results", requestId + ".json");
            if (!File.Exists(path))
            {
                return null;
            }

            var data = AIBridgeJson.DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
            return CommandResultFromDictionary(data);
        }

        public static string FormatCodeResult(CommandResult result)
        {
            if (result == null)
            {
                return "No result";
            }

            var lines = new List<string>
            {
                "success: " + result.success
            };
            if (!string.IsNullOrWhiteSpace(result.error))
            {
                lines.Add("error: " + result.error);
            }

            var data = result.data as Dictionary<string, object>;
            if (data != null)
            {
                AddValue(lines, data, "status");
                AddValue(lines, data, "source");
                AddValue(lines, data, "elapsedMs");
                AddValue(lines, data, "returnValue");
                AddCollection(lines, data, "logs");
                AddCollection(lines, data, "compileErrors");
                AddCollection(lines, data, "diagnostics");
                AddValue(lines, data, "exception");
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        public static string GetCodeExecutionCacheDirectoryFullPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, CodeExecutionCacheDirectory));
        }

        private static string PrepareCodeExecutionFile(string scriptPath)
        {
            var fullPath = Path.GetFullPath(scriptPath);
            var codeRoot = GetCodeExecutionCacheDirectoryFullPath();
            if (!Directory.Exists(codeRoot))
            {
                Directory.CreateDirectory(codeRoot);
            }

            if (IsDirectChildOfDirectory(fullPath, codeRoot))
            {
                return fullPath;
            }

            var extension = Path.GetExtension(fullPath);
            var fileName = "scripts_tab_" + SanitizeFileName(Path.GetFileNameWithoutExtension(fullPath)) + "_" + ComputeShortHash(fullPath) + extension;
            var executionFile = Path.Combine(codeRoot, fileName);
            File.Copy(fullPath, executionFile, true);
            return executionFile;
        }

        private static bool IsSupportedScriptPath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".csx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCSharpScriptPath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".csx", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDirectChildOfDirectory(string filePath, string directoryPath)
        {
            var parent = Directory.GetParent(filePath);
            return parent != null
                   && string.Equals(
                       parent.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder();
            foreach (var c in value ?? string.Empty)
            {
                builder.Append(invalid.Contains(c) ? '_' : c);
            }

            return builder.Length == 0 ? "script" : builder.ToString();
        }

        private static string ComputeShortHash(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder();
                for (var i = 0; i < 6 && i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static void AddValue(List<string> lines, Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null)
            {
                return;
            }

            lines.Add(key + ": " + FormatValue(value));
        }

        private static void AddCollection(List<string> lines, Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null)
            {
                return;
            }

            var items = value as IEnumerable<object>;
            if (items == null)
            {
                lines.Add(key + ": " + FormatValue(value));
                return;
            }

            var list = items.ToList();
            if (list.Count == 0)
            {
                return;
            }

            lines.Add(key + ":");
            foreach (var item in list)
            {
                lines.Add("  - " + FormatValue(item));
            }
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                return AIBridgeJson.Serialize(dictionary);
            }

            var collection = value as IEnumerable<object>;
            if (collection != null)
            {
                return AIBridgeJson.Serialize(collection.ToList());
            }

            return value.ToString();
        }

        private static CommandResult CommandResultFromDictionary(Dictionary<string, object> data)
        {
            if (data == null)
            {
                return null;
            }

            object value;
            var result = new CommandResult();
            if (data.TryGetValue("id", out value) && value != null)
            {
                result.id = value.ToString();
            }

            if (data.TryGetValue("success", out value) && value != null)
            {
                result.success = Convert.ToBoolean(value);
            }

            if (data.TryGetValue("data", out value))
            {
                result.data = value;
            }

            if (data.TryGetValue("error", out value) && value != null)
            {
                result.error = value.ToString();
            }

            if (data.TryGetValue("executionTime", out value) && value != null)
            {
                result.executionTime = Convert.ToInt64(value);
            }

            return result;
        }
    }
}
