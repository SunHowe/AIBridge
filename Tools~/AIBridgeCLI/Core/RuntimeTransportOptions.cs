using System;

namespace AIBridgeCLI.Core
{
    public enum RuntimeTransportKind
    {
        File,
        Adb,
        Http
    }

    public sealed class RuntimeTransportOptions
    {
        public const string DefaultTarget = "latest";
        public const string TransportEnvironment = "AIBRIDGE_RUNTIME_TRANSPORT";
        public const string AdbPathEnvironment = "AIBRIDGE_ADB";
        public const string AdbDeviceEnvironment = "AIBRIDGE_RUNTIME_DEVICE";
        public const string AndroidPackageEnvironment = "AIBRIDGE_ANDROID_PACKAGE";
        public const string DevicePathEnvironment = "AIBRIDGE_RUNTIME_DEVICE_PATH";
        public const string DefaultHttpUrl = "http://127.0.0.1:27182";
        public const string HttpUrlEnvironment = "AIBRIDGE_RUNTIME_URL";
        public const string TokenEnvironment = "AIBRIDGE_RUNTIME_TOKEN";

        public RuntimeTransportKind Kind { get; private set; }
        public string RuntimeDirectory { get; private set; }
        public string Target { get; private set; }
        public int TimeoutMs { get; private set; }
        public int PollIntervalMs { get; private set; }
        public string AdbPath { get; private set; }
        public string DeviceSerial { get; private set; }
        public string AndroidPackageName { get; private set; }
        public string DevicePath { get; private set; }
        public string HttpUrl { get; private set; }
        public string Token { get; private set; }

        private RuntimeTransportOptions()
        {
        }

        public static RuntimeTransportOptions Create(
            string transport,
            string runtimeDirectoryOverride,
            string target,
            int timeoutMs,
            int pollIntervalMs)
        {
            var resolvedTransport = ResolveTransportName(transport);
            var commandLineOptions = ReadCommandLineOptions();
            return new RuntimeTransportOptions
            {
                Kind = ParseTransportKind(resolvedTransport),
                RuntimeDirectory = RuntimePathHelper.ResolveRuntimeDirectory(runtimeDirectoryOverride),
                Target = string.IsNullOrWhiteSpace(target) ? DefaultTarget : target,
                TimeoutMs = timeoutMs,
                PollIntervalMs = pollIntervalMs,
                AdbPath = ResolveOption(commandLineOptions, "adb", AdbPathEnvironment),
                DeviceSerial = ResolveOption(commandLineOptions, "device", AdbDeviceEnvironment),
                AndroidPackageName = ResolveOption(commandLineOptions, "package", AndroidPackageEnvironment),
                DevicePath = ResolveOption(commandLineOptions, "device-path", DevicePathEnvironment),
                HttpUrl = NormalizeHttpUrl(ResolveOption(commandLineOptions, "url", HttpUrlEnvironment)),
                Token = ResolveOption(commandLineOptions, "token", TokenEnvironment)
            };
        }

        private static string ResolveTransportName(string transport)
        {
            if (!string.IsNullOrWhiteSpace(transport))
            {
                return transport;
            }

            var envTransport = Environment.GetEnvironmentVariable(TransportEnvironment);
            return string.IsNullOrWhiteSpace(envTransport) ? "file" : envTransport;
        }

        private static RuntimeTransportKind ParseTransportKind(string transport)
        {
            if (string.Equals(transport, "file", StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeTransportKind.File;
            }

            if (string.Equals(transport, "adb", StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeTransportKind.Adb;
            }

            if (string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeTransportKind.Http;
            }

            throw new ArgumentException($"Unsupported runtime transport: {transport}. Supported transports: file, adb, http.");
        }

        private static string ResolveOption(System.Collections.Generic.Dictionary<string, string> options, string key, string environmentName)
        {
            if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var envValue = Environment.GetEnvironmentVariable(environmentName);
            return string.IsNullOrWhiteSpace(envValue) ? null : envValue;
        }

        private static System.Collections.Generic.Dictionary<string, string> ReadCommandLineOptions()
        {
            var options = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrEmpty(arg) || !arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var key = arg.Substring(2);
                var value = "true";
                var equalsIndex = key.IndexOf('=');
                if (equalsIndex >= 0)
                {
                    value = key.Substring(equalsIndex + 1);
                    key = key.Substring(0, equalsIndex);
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    i++;
                }

                options[key] = value;
            }

            return options;
        }

        private static string NormalizeHttpUrl(string value)
        {
            var url = string.IsNullOrWhiteSpace(value) ? DefaultHttpUrl : value.Trim();
            return url.TrimEnd('/');
        }
    }
}
