using System;
using System.IO;

namespace AIBridge.Editor
{
    /// <summary>
    /// Removes legacy cache directory left by versions before the .aibridge migration.
    /// </summary>
    internal static class LegacyCacheDirectoryCleaner
    {
        private const string LegacyCacheDirectoryName = "AIBridgeCache";

        public static bool CleanupIfNeeded(string projectRoot, string currentBridgeDirectory)
        {
            if (string.IsNullOrEmpty(projectRoot) || string.IsNullOrEmpty(currentBridgeDirectory))
            {
                return false;
            }

            var legacyDirectory = Path.Combine(projectRoot, LegacyCacheDirectoryName);
            if (!Directory.Exists(legacyDirectory))
            {
                return false;
            }

            if (!Directory.Exists(currentBridgeDirectory))
            {
                AIBridgeLogger.LogDebug("Skipped legacy cache cleanup because .aibridge is not initialized yet.");
                return false;
            }

            var fullProjectRoot = NormalizeFullPath(projectRoot);
            var fullLegacyDirectory = NormalizeFullPath(legacyDirectory);
            var fullCurrentBridgeDirectory = NormalizeFullPath(currentBridgeDirectory);
            if (!IsDirectChildOf(fullLegacyDirectory, fullProjectRoot)
                || string.Equals(fullLegacyDirectory, fullCurrentBridgeDirectory, StringComparison.OrdinalIgnoreCase))
            {
                AIBridgeLogger.LogWarning($"Skipped unsafe legacy cache cleanup path: {legacyDirectory}");
                return false;
            }

            try
            {
                // 旧目录只用于历史版本的命令、结果、截图和 CLI 缓存；新目录已初始化后可安全清理。
                Directory.Delete(fullLegacyDirectory, true);
                AIBridgeLogger.LogInfo($"Removed legacy cache directory: {fullLegacyDirectory}");
                return true;
            }
            catch (Exception ex)
            {
                AIBridgeLogger.LogWarning($"Failed to remove legacy cache directory '{fullLegacyDirectory}': {ex.Message}");
                return false;
            }
        }

        private static string NormalizeFullPath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsDirectChildOf(string childPath, string parentPath)
        {
            var parent = Directory.GetParent(childPath);
            return parent != null && string.Equals(parent.FullName, parentPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
