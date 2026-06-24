using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AIBridge.Internal.Json;
using UnityEngine;

namespace AIBridge.Editor
{
    internal static class AIBridgeRootDirectoryUtility
    {
        public const string PackageName = "cn.lys.aibridge";
        public const string DefaultAIBridgeRootDirectory = "Packages/" + PackageName;
        public const string RootDirectoryMirrorRelativePath = ".aibridge/aibridge-root.json";

        public static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }

        public static string GetDefaultAIBridgeRootDirectory(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                DefaultAIBridgeRootDirectory.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static string GetAIBridgeRootDirecotry(string projectRoot)
        {
            return GetAIBridgeRootDirectory(projectRoot);
        }

        public static string GetAIBridgeRootDirectory(string projectRoot)
        {
            string customRoot;
            if (TryResolveExistingCustomRootDirectory(projectRoot, out customRoot))
            {
                return customRoot;
            }

            return GetDefaultAIBridgeRootDirectory(projectRoot);
        }

        public static string GetResolvedAIBridgeRootDirectory(string projectRoot)
        {
            string customRoot;
            if (TryResolveExistingCustomRootDirectory(projectRoot, out customRoot))
            {
                return customRoot;
            }

            var defaultRoot = GetDefaultAIBridgeRootDirectory(projectRoot);
            if (Directory.Exists(defaultRoot))
            {
                return defaultRoot;
            }

            var packageInfoRoot = GetPackageInfoRoot();
            if (Directory.Exists(packageInfoRoot))
            {
                return packageInfoRoot;
            }

            var packageCacheRoot = GetPackageCacheRoot(projectRoot);
            if (!string.IsNullOrEmpty(packageCacheRoot))
            {
                return packageCacheRoot;
            }

            if (IsAIBridgePackageRoot(projectRoot))
            {
                return projectRoot;
            }

            return defaultRoot;
        }

        public static List<string> GetCandidateRootDirectories(string projectRoot)
        {
            var roots = new List<string>();
            string customRoot;
            if (TryResolveExistingCustomRootDirectory(projectRoot, out customRoot))
            {
                AddUniqueRoot(roots, customRoot);
                return roots;
            }

            var defaultRoot = GetDefaultAIBridgeRootDirectory(projectRoot);
            if (Directory.Exists(defaultRoot))
            {
                AddUniqueRoot(roots, defaultRoot);
            }

            var packageInfoRoot = GetPackageInfoRoot();
            if (Directory.Exists(packageInfoRoot))
            {
                AddUniqueRoot(roots, packageInfoRoot);
            }

            var packageCacheRoot = GetPackageCacheRoot(projectRoot);
            if (!string.IsNullOrEmpty(packageCacheRoot))
            {
                AddUniqueRoot(roots, packageCacheRoot);
            }

            if (IsAIBridgePackageRoot(projectRoot))
            {
                AddUniqueRoot(roots, projectRoot);
            }

            if (roots.Count == 0)
            {
                AddUniqueRoot(roots, defaultRoot);
            }

            return roots;
        }

        public static string GetRootRelativePath(string projectRoot, string relativePath)
        {
            return Path.Combine(
                GetResolvedAIBridgeRootDirectory(projectRoot),
                NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar));
        }

        public static string ResolveRootRelativeDirectory(string projectRoot, string relativePath)
        {
            var path = GetRootRelativePath(projectRoot, relativePath);
            return Directory.Exists(path) ? path : null;
        }

        public static string ResolveRootRelativeFilePath(string projectRoot, string relativePath)
        {
            var path = GetRootRelativePath(projectRoot, relativePath);
            return File.Exists(path) ? path : null;
        }

        public static string GetRootRelativeDisplayPath(string projectRoot, string relativePath)
        {
            return ToDisplayPath(projectRoot, GetRootRelativePath(projectRoot, relativePath));
        }

        public static string ToProjectRelativeOrFullPath(string projectRoot, string selectedDirectory)
        {
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                return string.Empty;
            }

            var fullPath = Path.GetFullPath(selectedDirectory);
            var relative = TryMakeRelative(projectRoot, fullPath);
            return NormalizeConfiguredDirectory(relative ?? fullPath);
        }

        public static string NormalizeConfiguredDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }

            return TrimTrailingSeparators(directory.Trim().Replace('\\', '/'));
        }

        public static bool IsValidConfiguredDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            if (Path.IsPathRooted(directory))
            {
                return true;
            }

            return !directory.Split('/').Any(part => part == "..");
        }

        public static string ResolveConfiguredDirectory(string projectRoot, string configuredDirectory)
        {
            var normalized = NormalizeConfiguredDirectory(configuredDirectory);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            var path = Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
            return Path.GetFullPath(path);
        }

        public static bool IsAIBridgePackageRoot(string directory)
        {
            return !string.IsNullOrWhiteSpace(directory)
                && Directory.Exists(directory)
                && File.Exists(Path.Combine(directory, "package.json"))
                && (Directory.Exists(Path.Combine(directory, "Skill~"))
                    || Directory.Exists(Path.Combine(directory, "Templates~"))
                    || Directory.Exists(Path.Combine(directory, "Tools~")));
        }

        public static void WriteRootDirectoryMirrorNoThrow()
        {
            try
            {
                WriteRootDirectoryMirror();
            }
            catch (Exception ex)
            {
                AIBridgeLogger.LogWarning("[AIBridge] Failed to write root directory mirror: " + ex.Message);
            }
        }

        public static void WriteRootDirectoryMirror()
        {
            var projectRoot = GetProjectRoot();
            if (string.IsNullOrEmpty(projectRoot))
            {
                return;
            }

            var settings = AIBridgeProjectSettings.Instance;
            var mirrorPath = Path.Combine(projectRoot, RootDirectoryMirrorRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(mirrorPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var rootDirectory = GetAIBridgeRootDirectory(projectRoot);
            var resolvedRootDirectory = GetResolvedAIBridgeRootDirectory(projectRoot);
            var json = AIBridgeJson.Serialize(new Dictionary<string, object>
            {
                { "schemaVersion", 1 },
                { "packageName", PackageName },
                { "defaultAIBridgeRootDirectory", DefaultAIBridgeRootDirectory },
                { "useCustomAIBridgeRootDirectory", settings.UseCustomAIBridgeRootDirectory },
                { "customAIBridgeRootDirectory", settings.CustomAIBridgeRootDirectory },
                { "aibridgeRootDirecotry", NormalizePath(rootDirectory) },
                { "aibridgeRootDirectory", NormalizePath(rootDirectory) },
                { "resolvedAIBridgeRootDirectory", NormalizePath(resolvedRootDirectory) }
            }, pretty: true);
            File.WriteAllText(mirrorPath, json, new UTF8Encoding(false));
        }

        public static string ToDisplayPath(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var fullPath = Path.GetFullPath(path);
            var relative = TryMakeRelative(projectRoot, fullPath);
            return NormalizePath(relative ?? fullPath);
        }

        public static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        private static bool TryResolveExistingCustomRootDirectory(string projectRoot, out string customRoot)
        {
            customRoot = null;
            var settings = AIBridgeProjectSettings.Instance;
            if (!settings.UseCustomAIBridgeRootDirectory)
            {
                return false;
            }

            var configured = settings.CustomAIBridgeRootDirectory;
            if (string.IsNullOrEmpty(configured) || !IsValidConfiguredDirectory(configured))
            {
                return false;
            }

            var resolved = ResolveConfiguredDirectory(projectRoot, configured);
            if (Directory.Exists(resolved))
            {
                customRoot = resolved;
                return true;
            }

            return false;
        }

        private static string GetPackageInfoRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(DefaultAIBridgeRootDirectory);
            return packageInfo == null ? null : packageInfo.resolvedPath;
        }

        private static string GetPackageCacheRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            var packageCache = Path.Combine(projectRoot, "Library", "PackageCache");
            if (!Directory.Exists(packageCache))
            {
                return null;
            }

            var directCachePackage = Path.Combine(packageCache, PackageName);
            if (IsAIBridgePackageRoot(directCachePackage))
            {
                return directCachePackage;
            }

            foreach (var directory in Directory.EnumerateDirectories(packageCache, PackageName + "@*", SearchOption.TopDirectoryOnly))
            {
                if (IsAIBridgePackageRoot(directory))
                {
                    return directory;
                }
            }

            return null;
        }

        private static void AddUniqueRoot(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            for (var i = 0; i < roots.Count; i++)
            {
                if (string.Equals(Path.GetFullPath(roots[i]), fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            roots.Add(path);
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return string.IsNullOrEmpty(relativePath)
                ? string.Empty
                : relativePath.Replace('\\', '/').Trim('/');
        }

        private static string TryMakeRelative(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return normalizedPath.Substring(prefix.Length);
        }

        private static string TrimTrailingSeparators(string path)
        {
            var root = Path.GetPathRoot(path);
            var normalizedRoot = string.IsNullOrEmpty(root) ? null : root.Replace('\\', '/');
            while (path.Length > 1
                   && (path.EndsWith("/", StringComparison.Ordinal) || path.EndsWith("\\", StringComparison.Ordinal))
                   && !string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - 1);
            }

            return path;
        }
    }
}
