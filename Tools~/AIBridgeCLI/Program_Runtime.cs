using System;
using System.Collections.Generic;
using System.Globalization;
using AIBridgeCLI.Core;
using Newtonsoft.Json;

namespace AIBridgeCLI
{
    partial class Program
    {
        private const int RuntimePerfTimeoutPaddingMs = 3000;

        static int HandleRuntimeCommand(ParsedArgs parsed, CommandRequest request, int timeout, bool noWait, OutputMode outputMode)
        {
            parsed.Options.TryGetValue("runtime-dir", out var runtimeDirectory);
            parsed.Options.TryGetValue("target", out var target);
            parsed.Options.TryGetValue("transport", out var transport);

            var actualTimeout = ResolveRuntimeTimeout(parsed, request, timeout);
            var sender = new RuntimeCommandSender(runtimeDirectory, target, actualTimeout, transport: transport);
            CommandResult result;

            if (noWait)
            {
                result = sender.TrySendCommandNoWait(request);
                if (outputMode == OutputMode.Pretty)
                {
                    OutputFormatter.PrintResult(result, outputMode, includeIdInRaw: false);
                }
                else
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new
                    {
                        success = result.success,
                        error = result.error,
                        data = result.data
                    }, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
                }

                return result.success ? 0 : 1;
            }

            result = sender.SendCommand(request);
            OutputFormatter.PrintResult(result, outputMode, includeIdInRaw: false);
            return result.success ? 0 : 1;
        }

        private static int ResolveRuntimeTimeout(ParsedArgs parsed, CommandRequest request, int timeout)
        {
            if (parsed == null || request == null || parsed.Options.ContainsKey("timeout"))
            {
                return timeout;
            }

            var action = RuntimePathHelper.GetRuntimeAction(request);
            if (!string.Equals(action, "runtime.perf", StringComparison.OrdinalIgnoreCase))
            {
                return timeout;
            }

            var durationMs = ReadRuntimePerfDurationMs(request.@params);
            return Math.Max(timeout, durationMs + RuntimePerfTimeoutPaddingMs);
        }

        private static int ReadRuntimePerfDurationMs(Dictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                return 5000;
            }

            if (TryReadMilliseconds(parameters, "durationMs", out var durationMs))
            {
                return durationMs;
            }

            return TryReadMilliseconds(parameters, "duration", out durationMs) ? durationMs : 5000;
        }

        private static bool TryReadMilliseconds(Dictionary<string, object> parameters, string key, out int milliseconds)
        {
            milliseconds = 0;
            if (!parameters.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            if (value is int intValue)
            {
                milliseconds = intValue;
                return milliseconds > 0;
            }

            if (value is long longValue)
            {
                milliseconds = ClampRuntimeTimeout(longValue);
                return milliseconds > 0;
            }

            if (value is double doubleValue)
            {
                milliseconds = ClampRuntimeTimeout((long)Math.Round(doubleValue));
                return milliseconds > 0;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                milliseconds = ParseRuntimeTimeoutNumber(text.Substring(0, text.Length - 2), 1d);
                return milliseconds > 0;
            }

            if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                milliseconds = ParseRuntimeTimeoutNumber(text.Substring(0, text.Length - 1), 1000d);
                return milliseconds > 0;
            }

            milliseconds = ParseRuntimeTimeoutNumber(text, 1d);
            return milliseconds > 0;
        }

        private static int ParseRuntimeTimeoutNumber(string text, double multiplier)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return 0;
            }

            return ClampRuntimeTimeout((long)Math.Round(value * multiplier));
        }

        private static int ClampRuntimeTimeout(long value)
        {
            if (value <= 0L)
            {
                return 0;
            }

            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
