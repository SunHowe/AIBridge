using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Locator;

namespace AIBridgeCodeIndex
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            try
            {
                return RunAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            var options = CodeIndexOptions.Parse(args);
            if (string.IsNullOrWhiteSpace(options.ProjectRoot))
            {
                Console.Error.WriteLine("--project-root is required.");
                return 1;
            }

            options.ProjectRoot = Path.GetFullPath(options.ProjectRoot);
            if (!Directory.Exists(options.ProjectRoot))
            {
                Console.Error.WriteLine("Project root does not exist: " + options.ProjectRoot);
                return 1;
            }

            RegisterMSBuild();

            var server = new CodeIndexServer(options);
            await server.RunAsync();
            return 0;
        }

        private static void RegisterMSBuild()
        {
            if (MSBuildLocator.IsRegistered)
            {
                return;
            }

            var instance = MSBuildLocator.QueryVisualStudioInstances()
                .OrderByDescending(item => item.Version)
                .FirstOrDefault();
            if (instance != null)
            {
                MSBuildLocator.RegisterInstance(instance);
                return;
            }

            var sdkMsBuildPath = TryFindDotnetSdkMSBuildPath();
            if (!string.IsNullOrEmpty(sdkMsBuildPath))
            {
                MSBuildLocator.RegisterMSBuildPath(sdkMsBuildPath);
                return;
            }

            MSBuildLocator.RegisterDefaults();
        }

        private static string TryFindDotnetSdkMSBuildPath()
        {
            var fromListSdks = TryFindDotnetSdkFromListSdks();
            if (!string.IsNullOrEmpty(fromListSdks))
            {
                return fromListSdks;
            }

            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
            {
                var sdkRoot = Path.Combine(dotnetRoot, "sdk");
                var latest = GetLatestSdkDirectory(sdkRoot);
                if (!string.IsNullOrEmpty(latest))
                {
                    return latest;
                }
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                var sdkRoot = Path.Combine(programFiles, "dotnet", "sdk");
                var latest = GetLatestSdkDirectory(sdkRoot);
                if (!string.IsNullOrEmpty(latest))
                {
                    return latest;
                }
            }

            return null;
        }

        private static string TryFindDotnetSdkFromListSdks()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("--list-sdks");

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }

                var sdkPaths = output
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ParseDotnetSdkLine)
                    .Where(path => !string.IsNullOrEmpty(path) && Directory.Exists(path))
                    .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return sdkPaths.Count == 0 ? null : sdkPaths[0];
            }
            catch
            {
                return null;
            }
        }

        private static string ParseDotnetSdkLine(string line)
        {
            var bracketStart = line == null ? -1 : line.IndexOf('[');
            var bracketEnd = line == null ? -1 : line.IndexOf(']');
            if (bracketStart <= 0 || bracketEnd <= bracketStart)
            {
                return null;
            }

            var version = line.Substring(0, bracketStart).Trim();
            var root = line.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
            return string.IsNullOrEmpty(version) || string.IsNullOrEmpty(root)
                ? null
                : Path.Combine(root, version);
        }

        private static string GetLatestSdkDirectory(string sdkRoot)
        {
            if (string.IsNullOrWhiteSpace(sdkRoot) || !Directory.Exists(sdkRoot))
            {
                return null;
            }

            return Directory.GetDirectories(sdkRoot)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
    }
}
