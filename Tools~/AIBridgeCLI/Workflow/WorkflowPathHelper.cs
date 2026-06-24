using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBridgeCLI.Core;

namespace AIBridgeCLI.Workflow
{
    public static class WorkflowPathHelper
    {
        public const string RecipeExtension = ".aibridge-workflow.json";
        private const string TemplatesDirectoryName = "Templates~";
        private const string WorkflowsDirectoryName = "Workflows";
        private const string WorkflowRootName = "workflows";
        private const string PackageName = "cn.lys.aibridge";
        private const string RootDirectoryMirrorRelativePath = ".aibridge/aibridge-root.json";

        public static string GetPackageRoot()
        {
            var envPackageRoot = Environment.GetEnvironmentVariable("AIBRIDGE_PACKAGE_ROOT");
            if (IsPackageRoot(envPackageRoot))
            {
                return Path.GetFullPath(envPackageRoot);
            }

            var projectRoot = FindUnityProjectRoot();
            var customPackageRoot = FindCustomPackageRootFromUnityProject(projectRoot);
            if (!string.IsNullOrEmpty(customPackageRoot))
            {
                return customPackageRoot;
            }

            var packageRoot = FindPackageRootFromUnityProject(projectRoot);
            if (!string.IsNullOrEmpty(packageRoot))
            {
                return packageRoot;
            }

            var candidates = new List<string>
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory()
            };

            foreach (var candidate in candidates)
            {
                var found = SearchUpwardsForPackageRoot(candidate);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }

            return Directory.GetCurrentDirectory();
        }

        public static string GetBuiltInRecipesDirectory()
        {
            return Path.Combine(GetPackageRoot(), TemplatesDirectoryName, WorkflowsDirectoryName);
        }

        public static string GetWorkflowRootDirectory()
        {
            return Path.Combine(PathHelper.GetExchangeDirectory(), WorkflowRootName);
        }

        public static string GetProjectRecipesDirectory()
        {
            return Path.Combine(GetWorkflowRootDirectory(), "recipes");
        }

        public static string GetRunsDirectory()
        {
            return Path.Combine(GetWorkflowRootDirectory(), "runs");
        }

        public static string GetProjectRoot()
        {
            var exchange = Path.GetFullPath(PathHelper.GetExchangeDirectory());
            var exchangeInfo = new DirectoryInfo(exchange.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (exchangeInfo.Name.Equals(".aibridge", StringComparison.OrdinalIgnoreCase) && exchangeInfo.Parent != null)
            {
                return exchangeInfo.Parent.FullName;
            }

            return Directory.GetCurrentDirectory();
        }

        public static string GenerateRunId()
        {
            return "wf_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public static List<string> FindBuiltInRecipeFiles()
        {
            return FindRecipeFiles(GetBuiltInRecipesDirectory());
        }

        public static List<string> FindProjectRecipeFiles()
        {
            return FindRecipeFiles(GetProjectRecipesDirectory());
        }

        public static string ResolveRecipePath(string fileOrRecipe)
        {
            if (string.IsNullOrWhiteSpace(fileOrRecipe))
            {
                throw new ArgumentException("Missing workflow recipe path or name.");
            }

            var direct = ResolvePath(fileOrRecipe);
            if (File.Exists(direct))
            {
                return direct;
            }

            var name = Path.GetFileNameWithoutExtension(fileOrRecipe);
            if (name.EndsWith(".aibridge-workflow", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".aibridge-workflow".Length);
            }

            var fileName = name + RecipeExtension;
            var project = Path.Combine(GetProjectRecipesDirectory(), fileName);
            if (File.Exists(project))
            {
                return project;
            }

            var builtIn = Path.Combine(GetBuiltInRecipesDirectory(), fileName);
            if (File.Exists(builtIn))
            {
                return builtIn;
            }

            throw new FileNotFoundException("Workflow recipe was not found: " + fileOrRecipe);
        }

        public static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
        }

        public static string ToDisplayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var fullPath = Path.GetFullPath(path);
            var projectRoot = GetProjectRoot();
            var relative = TryMakeRelative(projectRoot, fullPath);
            return NormalizeSeparators(relative ?? fullPath);
        }

        public static string NormalizeSeparators(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        public static void EnsureWorkflowDirectories()
        {
            Directory.CreateDirectory(GetProjectRecipesDirectory());
            Directory.CreateDirectory(GetRunsDirectory());
        }

        private static List<string> FindRecipeFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return new List<string>();
            }

            return Directory.EnumerateFiles(directory, "*" + RecipeExtension, SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string SearchUpwardsForPackageRoot(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                return null;
            }

            var current = new DirectoryInfo(Path.GetFullPath(startPath));
            while (current != null)
            {
                if (IsPackageRoot(current.FullName))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return null;
        }

        private static bool IsPackageRoot(string directory)
        {
            return !string.IsNullOrWhiteSpace(directory)
                && Directory.Exists(directory)
                && File.Exists(Path.Combine(directory, "package.json"))
                && Directory.Exists(Path.Combine(directory, TemplatesDirectoryName));
        }

        private static string FindUnityProjectRoot()
        {
            var envProjectRoot = Environment.GetEnvironmentVariable("UNITY_PROJECT_ROOT");
            if (IsUnityProjectRoot(envProjectRoot))
            {
                return Path.GetFullPath(envProjectRoot);
            }

            var fromCwd = SearchUpwardsForUnityProjectRoot(Directory.GetCurrentDirectory());
            if (!string.IsNullOrEmpty(fromCwd))
            {
                return fromCwd;
            }

            return SearchUpwardsForUnityProjectRoot(AppDomain.CurrentDomain.BaseDirectory);
        }

        private static string SearchUpwardsForUnityProjectRoot(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                return null;
            }

            var current = new DirectoryInfo(Path.GetFullPath(startPath));
            while (current != null)
            {
                if (IsUnityProjectRoot(current.FullName))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return null;
        }

        private static bool IsUnityProjectRoot(string directory)
        {
            return !string.IsNullOrWhiteSpace(directory)
                && Directory.Exists(directory)
                && Directory.Exists(Path.Combine(directory, "Assets"))
                && File.Exists(Path.Combine(directory, "ProjectSettings", "ProjectSettings.asset"));
        }

        private static string FindPackageRootFromUnityProject(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            var embeddedPackage = Path.Combine(projectRoot, "Packages", PackageName);
            if (IsPackageRoot(embeddedPackage))
            {
                return embeddedPackage;
            }

            var packageCache = Path.Combine(projectRoot, "Library", "PackageCache");
            if (!Directory.Exists(packageCache))
            {
                return null;
            }

            var directCachePackage = Path.Combine(packageCache, PackageName);
            if (IsPackageRoot(directCachePackage))
            {
                return directCachePackage;
            }

            foreach (var directory in Directory.EnumerateDirectories(packageCache, PackageName + "@*", SearchOption.TopDirectoryOnly))
            {
                if (IsPackageRoot(directory))
                {
                    return directory;
                }
            }

            return null;
        }

        private static string FindCustomPackageRootFromUnityProject(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            var mirrorPath = Path.Combine(projectRoot, RootDirectoryMirrorRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(mirrorPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(mirrorPath);
                if (!ReadBool(json, "useCustomAIBridgeRootDirectory"))
                {
                    return null;
                }

                var configuredRoot = ReadString(json, "customAIBridgeRootDirectory");
                var resolvedRoot = ResolveConfiguredRoot(projectRoot, configuredRoot);
                if (Directory.Exists(resolvedRoot))
                {
                    return Path.GetFullPath(resolvedRoot);
                }

                var mirroredRoot = ReadString(json, "aibridgeRootDirecotry");
                if (string.IsNullOrWhiteSpace(mirroredRoot))
                {
                    mirroredRoot = ReadString(json, "aibridgeRootDirectory");
                }

                if (Directory.Exists(mirroredRoot))
                {
                    return Path.GetFullPath(mirroredRoot);
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string ResolveConfiguredRoot(string projectRoot, string configuredRoot)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
            {
                return null;
            }

            var normalized = configuredRoot.Trim().Replace('\\', '/');
            var path = Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
            return Path.GetFullPath(path);
        }

        private static bool ReadBool(string json, string key)
        {
            var value = ReadRawValue(json, key);
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(string json, string key)
        {
            var token = "\"" + key + "\"";
            var keyIndex = (json ?? string.Empty).IndexOf(token, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return null;
            }

            var colonIndex = json.IndexOf(':', keyIndex + token.Length);
            if (colonIndex < 0)
            {
                return null;
            }

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            if (valueStart >= json.Length || json[valueStart] != '"')
            {
                return null;
            }

            valueStart++;
            var builder = new System.Text.StringBuilder();
            for (var i = valueStart; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    builder.Append(json[i]);
                    continue;
                }

                if (c == '"')
                {
                    return builder.ToString();
                }

                builder.Append(c);
            }

            return null;
        }

        private static string ReadRawValue(string json, string key)
        {
            var token = "\"" + key + "\"";
            var keyIndex = (json ?? string.Empty).IndexOf(token, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return null;
            }

            var colonIndex = json.IndexOf(':', keyIndex + token.Length);
            if (colonIndex < 0)
            {
                return null;
            }

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            var valueEnd = valueStart;
            while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '\r' && json[valueEnd] != '\n' && json[valueEnd] != '}')
            {
                valueEnd++;
            }

            return json.Substring(valueStart, valueEnd - valueStart).Trim().Trim('"');
        }

        private static string TryMakeRelative(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return normalizedPath.Substring(normalizedRoot.Length);
        }
    }
}
