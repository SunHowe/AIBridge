using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBridgeCLI.Workflow
{
    public static class WorkflowInputResolver
    {
        private static readonly Regex TemplateRegex = new Regex(@"\{\{\s*([A-Za-z0-9_.-]+)\s*\}\}", RegexOptions.Compiled);

        public static JObject ResolveInputs(WorkflowRecipe recipe, string inputsValue)
        {
            var inputs = LoadInputs(inputsValue);
            if (recipe == null || recipe.Inputs == null)
            {
                return inputs;
            }

            foreach (var property in recipe.Inputs.Properties())
            {
                if (inputs.Property(property.Name, StringComparison.OrdinalIgnoreCase) != null)
                {
                    continue;
                }

                var defaultValue = ReadDefaultValue(property.Value);
                if (defaultValue != null)
                {
                    inputs[property.Name] = defaultValue.DeepClone();
                }
            }

            return inputs;
        }

        public static string ResolveTemplate(string text, JObject inputs)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return TemplateRegex.Replace(text, match =>
            {
                var key = match.Groups[1].Value;
                var value = ResolveValue(inputs, key);
                return value == null ? match.Value : TokenToCommandString(value);
            });
        }

        private static JObject LoadInputs(string inputsValue)
        {
            if (string.IsNullOrWhiteSpace(inputsValue))
            {
                return new JObject();
            }

            if (LooksLikeJson(inputsValue))
            {
                return ParseInputsJson(inputsValue, "inline JSON");
            }

            var resolved = WorkflowPathHelper.ResolvePath(inputsValue);
            if (File.Exists(resolved))
            {
                return ParseInputsJson(File.ReadAllText(resolved), "file: " + resolved);
            }

            return ParseInputsJson(inputsValue, "inline JSON");
        }

        private static JObject ParseInputsJson(string value, string source)
        {
            try
            {
                return JObject.Parse(value);
            }
            catch (JsonReaderException ex)
            {
                throw new ArgumentException(
                    "Failed to parse --inputs from " + source + ". Prefer passing a JSON file path, especially in PowerShell. JSON parse error: " + ex.Message,
                    ex);
            }
        }

        private static bool LooksLikeJson(string value)
        {
            // 内联 JSON 先解析，避免 Windows 路径解析被 JSON 标点提前打断。
            var trimmed = value == null ? null : value.TrimStart();
            return !string.IsNullOrEmpty(trimmed)
                && (trimmed[0] == '{' || trimmed[0] == '[');
        }

        private static JToken ReadDefaultValue(JToken inputDefinition)
        {
            if (inputDefinition == null || inputDefinition.Type == JTokenType.Null)
            {
                return null;
            }

            var obj = inputDefinition as JObject;
            if (obj != null && obj.TryGetValue("default", StringComparison.OrdinalIgnoreCase, out var defaultValue))
            {
                return defaultValue;
            }

            if (obj == null)
            {
                return inputDefinition;
            }

            return null;
        }

        private static JToken ResolveValue(JObject inputs, string key)
        {
            if (inputs == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (key.StartsWith("inputs.", StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring("inputs.".Length);
            }

            var current = (JToken)inputs;
            foreach (var segment in key.Split('.'))
            {
                var obj = current as JObject;
                if (obj == null || !obj.TryGetValue(segment, StringComparison.OrdinalIgnoreCase, out current))
                {
                    return null;
                }
            }

            return current;
        }

        private static string TokenToCommandString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            if (token.Type == JTokenType.Array || token.Type == JTokenType.Object)
            {
                return token.ToString(Newtonsoft.Json.Formatting.None);
            }

            return token.ToString();
        }
    }
}
