using System;
using AIBridgeCLI.Core;
using Newtonsoft.Json;

namespace AIBridgeCLI
{
    partial class Program
    {
        static int HandleRuntimeCommand(ParsedArgs parsed, CommandRequest request, int timeout, bool noWait, OutputMode outputMode)
        {
            parsed.Options.TryGetValue("runtime-dir", out var runtimeDirectory);
            parsed.Options.TryGetValue("target", out var target);
            parsed.Options.TryGetValue("transport", out var transport);

            var sender = new RuntimeCommandSender(runtimeDirectory, target, timeout, transport: transport);
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
    }
}
