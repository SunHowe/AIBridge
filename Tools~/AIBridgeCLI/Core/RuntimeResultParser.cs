using System;
using Newtonsoft.Json.Linq;

namespace AIBridgeCLI.Core
{
    public static class RuntimeResultParser
    {
        public static CommandResult Parse(string requestId, string resultJson)
        {
            try
            {
                var token = JObject.Parse(resultJson);
                return new CommandResult
                {
                    id = ReadString(token, "id") ?? ReadString(token, "CommandId") ?? requestId,
                    success = ReadBool(token, "success") ?? ReadBool(token, "Success") ?? false,
                    error = ReadString(token, "error") ?? ReadString(token, "Error"),
                    data = token["data"] ?? token["Data"],
                    executionTime = ReadLong(token, "executionTime") ?? ReadLong(token, "ExecutionTime")
                };
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    id = requestId,
                    success = false,
                    error = "Failed to parse runtime result: " + ex.Message,
                    data = resultJson
                };
            }
        }

        private static string ReadString(JObject token, string name)
        {
            return token.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value) ? value.Value<string>() : null;
        }

        private static bool? ReadBool(JObject token, string name)
        {
            if (!token.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value))
            {
                return null;
            }

            return value.Type == JTokenType.Boolean ? value.Value<bool>() : (bool?)null;
        }

        private static long? ReadLong(JObject token, string name)
        {
            if (!token.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value))
            {
                return null;
            }

            return value.Type == JTokenType.Integer ? value.Value<long>() : (long?)null;
        }
    }
}
