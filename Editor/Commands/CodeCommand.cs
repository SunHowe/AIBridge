using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AIBridge.Internal.Json;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    /// <summary>
    /// 受控版临时代码执行命令。默认关闭，且必须由 CLI 显式传入 allowExperimental。
    /// </summary>
    public class CodeCommand : ICommand
    {
        private const string ExecuteAction = "execute";
        private const string CodeDirectoryName = "code";
        private const string CompiledDirectoryName = ".compiled";
        private const string FallbackCompilerProcessName = "dotnet";
        private const int DefaultTimeoutMs = 5000;
        private const int MinTimeoutMs = 1000;
        private const int MaxTimeoutMs = 60000;
        private const int MaxInlineCodeLength = 4000;
        private const long MaxSourceFileBytes = 512 * 1024;
        private const string CSharpVersion2019 = "7.3";
        private const string CSharpVersion2020 = "8.0";
        private const string CSharpVersion2021OrNewer = "9.0";

        private static CodeAsyncOperation _activeOperation;

        public string Type => "code";
        public bool RequiresRefresh => false;

        public string SkillDescription => @"### `code execute` - Controlled Temporary C# Execution

Experimental and disabled by default. Enable it in **AIBridge/Settings -> Basic -> Enable Code Execution** before use. CLI calls must also pass `--allow-experimental true`.

```bash
$CLI code execute --file "".aibridge/code/check.csx"" --allow-experimental true --timeout 5000
$CLI code execute --code ""Debug.Log(\""hello\""); return 123;"" --allow-experimental true
```

**Rules:**
- Unity-side project setting cannot be bypassed by CLI parameters.
- `--file` must point to `.aibridge/code/*.cs` or `.aibridge/code/*.csx`.
- `--code` is intended for short snippets only.
- Prefer file mode for one-off Editor generation scripts that create complex Prefabs, scenes, effects, or assets.
- For generation scripts, keep output under a clear folder such as `Assets/AIBridgeGenerated/<TaskName>/` and return structured result data.
- Snippets are wrapped as `object Execute()` or `Task<object> ExecuteAsync()` when `await` is present.
- Result data includes `enabled`, `source`, `elapsedMs`, `returnValue`, `logs`, `compileErrors`, and `exception` when applicable.
- Use this only for trusted projects/callers; it is not a replacement for `compile unity` or `test run`.";

        public CommandResult Execute(CommandRequest request)
        {
            var action = request.GetParam("action", ExecuteAction);
            if (!string.Equals(action, ExecuteAction, StringComparison.OrdinalIgnoreCase))
            {
                return CommandResult.Failure(request.id, $"Unknown action: {action}. Supported: execute");
            }

            try
            {
                return ExecuteCode(request);
            }
            catch (Exception ex)
            {
                return CommandResult.FromException(request.id, ex);
            }
        }

        private CommandResult ExecuteCode(CommandRequest request)
        {
            var settings = AIBridgeProjectSettings.Instance;
            if (!settings.EnableCodeExecution || !settings.CodeExecutionRiskAccepted)
            {
                return FailureWithData(
                    request.id,
                    "Code execution is disabled. Enable it in AIBridge/Settings -> Basic -> Enable Code Execution.",
                    BuildSettingsGateData(settings));
            }

            if (!request.GetParam("allowExperimental", false))
            {
                return FailureWithData(
                    request.id,
                    "code execute requires --allow-experimental true.",
                    new
                    {
                        enabled = true,
                        source = "none"
                    });
            }

            if (_activeOperation != null)
            {
                return CommandResult.Failure(request.id, "Another code execution is already running.");
            }

            SourceText sourceText;
            string sourceError;
            if (!TryResolveSource(request, out sourceText, out sourceError))
            {
                return FailureWithData(
                    request.id,
                    sourceError,
                    new
                    {
                        enabled = true,
                        source = "invalid"
                    });
            }

            var timeoutMs = Mathf.Clamp(request.GetParam("timeout", DefaultTimeoutMs), MinTimeoutMs, MaxTimeoutMs);
            var stopwatch = Stopwatch.StartNew();
            var session = new CodeExecutionSession(request.id, sourceText, timeoutMs, stopwatch);

            try
            {
                string assemblyPath;
                List<string> compileErrors;
                if (!CompileSource(session, out assemblyPath, out compileErrors))
                {
                    stopwatch.Stop();
                    return FailureWithData(
                        request.id,
                        "Code compilation failed.",
                        BuildResultData(session, null, compileErrors, null, ContainsTimeoutError(compileErrors) ? (bool?)true : null));
                }

                session.CompiledAssemblyPath = assemblyPath;
                Application.logMessageReceived += session.OnLogMessageReceived;

                var invocation = InvokeCompiledCode(assemblyPath, sourceText.ClassName);
                if (invocation.Task != null)
                {
                    _activeOperation = new CodeAsyncOperation(session, invocation.Task);
                    EditorApplication.update -= OnAsyncUpdate;
                    EditorApplication.update += OnAsyncUpdate;
                    return null;
                }

                Application.logMessageReceived -= session.OnLogMessageReceived;
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    return FailureWithData(
                        request.id,
                        "Code execution timed out after " + timeoutMs + "ms.",
                        BuildResultData(session, null, new List<string>(), null, true));
                }

                return CommandResult.Success(request.id, BuildResultData(session, NormalizeReturnValue(invocation.ReturnValue), new List<string>(), null, null));
            }
            catch (Exception ex)
            {
                Application.logMessageReceived -= session.OnLogMessageReceived;
                stopwatch.Stop();
                return FailureWithData(request.id, "Code execution failed.", BuildResultData(session, null, new List<string>(), BuildExceptionInfo(ex), null));
            }
        }

        private static bool TryResolveSource(CommandRequest request, out SourceText sourceText, out string error)
        {
            sourceText = null;
            error = null;

            var file = request.GetParam<string>("file", null);
            var inlineCode = request.GetParam<string>("code", null);
            var hasFile = !string.IsNullOrWhiteSpace(file);
            var hasInlineCode = !string.IsNullOrWhiteSpace(inlineCode);
            if (hasFile == hasInlineCode)
            {
                error = "Provide exactly one source: --file or --code.";
                return false;
            }

            var projectRoot = GetProjectRoot();
            var codeRoot = Path.GetFullPath(Path.Combine(projectRoot, ".aibridge", CodeDirectoryName));

            if (hasFile)
            {
                var fullPath = ResolveCodeFilePath(file);
                if (!IsSameOrChildPath(codeRoot, fullPath))
                {
                    error = "Code file must be under .aibridge/code.";
                    return false;
                }

                var parentDirectory = Directory.GetParent(fullPath);
                if (parentDirectory == null || !string.Equals(parentDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), codeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    error = "Code file must be directly under .aibridge/code.";
                    return false;
                }

                var extension = Path.GetExtension(fullPath);
                if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".csx", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Code file must be .cs or .csx.";
                    return false;
                }

                if (!File.Exists(fullPath))
                {
                    error = "Code file not found: " + fullPath;
                    return false;
                }

                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length > MaxSourceFileBytes)
                {
                    error = "Code file is too large. Maximum size is " + MaxSourceFileBytes + " bytes.";
                    return false;
                }

                sourceText = BuildSourceText(File.ReadAllText(fullPath, Encoding.UTF8), "file", fullPath);
                return true;
            }

            if (inlineCode.Length > MaxInlineCodeLength)
            {
                error = "Inline code is too long. Use a file under .aibridge/code instead.";
                return false;
            }

            sourceText = BuildSourceText(inlineCode, "inline", "inline");
            return true;
        }

        private static SourceText BuildSourceText(string code, string sourceKind, string sourcePath)
        {
            var className = "AIBridgeCode_" + Guid.NewGuid().ToString("N");
            var containsAwait = code.IndexOf("await", StringComparison.Ordinal) >= 0;
            var leadingUsings = ExtractLeadingUsings(ref code);
            var wrappedSource = WrapCode(className, code, leadingUsings, containsAwait);
            return new SourceText
            {
                Kind = sourceKind,
                Path = sourcePath,
                Code = code,
                WrappedCode = wrappedSource,
                ClassName = className,
                IsAsync = containsAwait
            };
        }

        private static List<string> ExtractLeadingUsings(ref string code)
        {
            var result = new List<string>();
            var reader = new StringReader(code ?? string.Empty);
            var body = new StringBuilder();
            string line;
            var stillReadingUsings = true;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (stillReadingUsings && (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//", StringComparison.Ordinal)))
                {
                    continue;
                }

                if (stillReadingUsings && trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(";", StringComparison.Ordinal))
                {
                    result.Add(trimmed);
                    continue;
                }

                stillReadingUsings = false;
                body.AppendLine(line);
            }

            code = body.ToString();
            return result;
        }

        private static string WrapCode(string className, string body, IEnumerable<string> leadingUsings, bool isAsync)
        {
            var builder = new StringBuilder();
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Collections;");
            builder.AppendLine("using System.Collections.Generic;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine("using UnityEditor;");
            builder.AppendLine("using AIBridge.Editor;");
            foreach (var usingLine in leadingUsings)
            {
                builder.AppendLine(usingLine);
            }

            builder.AppendLine("public static class " + className);
            builder.AppendLine("{");
            builder.AppendLine(isAsync
                ? "    public static async Task<object> ExecuteAsync()"
                : "    public static object Execute()");
            builder.AppendLine("    {");
            builder.AppendLine(body ?? string.Empty);
            builder.AppendLine("        return null;");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static bool CompileSource(CodeExecutionSession session, out string assemblyPath, out List<string> compileErrors)
        {
            compileErrors = new List<string>();
            assemblyPath = null;

            var projectRoot = GetProjectRoot();
            var compiledDir = Path.Combine(projectRoot, ".aibridge", CodeDirectoryName, CompiledDirectoryName);
            if (!Directory.Exists(compiledDir))
            {
                Directory.CreateDirectory(compiledDir);
            }

            var fileStem = session.Source.ClassName;
            var sourcePath = Path.Combine(compiledDir, fileStem + ".generated.cs");
            assemblyPath = Path.Combine(compiledDir, fileStem + ".dll");
            var responsePath = Path.Combine(compiledDir, fileStem + ".rsp");
            File.WriteAllText(sourcePath, session.Source.WrappedCode, Encoding.UTF8);
            File.WriteAllText(responsePath, BuildCompilerResponseFile(sourcePath, assemblyPath, GetSupportedCSharpLanguageVersion()), Encoding.UTF8);

            CompilerInvocation compiler;
            if (!TryResolveCompiler(out compiler))
            {
                compileErrors.Add("C# compiler was not found in the Unity installation.");
                return false;
            }

            var output = RunCompilerProcess(compiler, responsePath, session.TimeoutMs, compileErrors);
            if (!string.IsNullOrEmpty(output))
            {
                session.CompilerOutput = output;
            }

            return File.Exists(assemblyPath) && compileErrors.Count == 0;
        }

        private static bool ContainsTimeoutError(IEnumerable<string> errors)
        {
            if (errors == null)
            {
                return false;
            }

            foreach (var error in errors)
            {
                if (!string.IsNullOrEmpty(error)
                    && error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal static string GetSupportedCSharpLanguageVersion()
        {
            return GetSupportedCSharpLanguageVersion(Application.unityVersion);
        }

        internal static string GetSupportedCSharpLanguageVersion(string unityVersion)
        {
            Version version;
            if (!TryParseUnityVersion(unityVersion, out version))
            {
                return CSharpVersion2019;
            }

            if (version.Major >= 2022 || version.Major >= 6000)
            {
                return CSharpVersion2021OrNewer;
            }

            if (version.Major == 2021)
            {
                return version.Minor >= 2 ? CSharpVersion2021OrNewer : CSharpVersion2020;
            }

            if (version.Major == 2020)
            {
                return version.Minor >= 2 ? CSharpVersion2020 : CSharpVersion2019;
            }

            return CSharpVersion2019;
        }

        private static bool TryParseUnityVersion(string unityVersion, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(unityVersion))
            {
                return false;
            }

            var builder = new StringBuilder();
            for (var i = 0; i < unityVersion.Length; i++)
            {
                var c = unityVersion[i];
                if (char.IsDigit(c) || c == '.')
                {
                    builder.Append(c);
                    continue;
                }

                break;
            }

            var versionText = builder.ToString().Trim('.');
            if (string.IsNullOrEmpty(versionText))
            {
                return false;
            }

            while (versionText.Split('.').Length < 2)
            {
                versionText += ".0";
            }

            return Version.TryParse(versionText, out version);
        }

        private static string BuildCompilerResponseFile(string sourcePath, string assemblyPath, string languageVersion)
        {
            var builder = new StringBuilder();
            builder.AppendLine("-nologo");
            builder.AppendLine("-target:library");
            builder.AppendLine("-langversion:" + languageVersion);
            builder.AppendLine("-unsafe-");
            builder.AppendLine("-out:\"" + assemblyPath + "\"");

            foreach (var reference in GetCompilationReferences())
            {
                builder.AppendLine("-reference:\"" + reference + "\"");
            }

            builder.AppendLine("\"" + sourcePath + "\"");
            return builder.ToString();
        }

        private static IEnumerable<string> GetCompilationReferences()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .Select(assembly =>
                {
                    try
                    {
                        return assembly.Location;
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryResolveCompiler(out CompilerInvocation compiler)
        {
            compiler = null;
            var contentsPath = EditorApplication.applicationContentsPath;
            var candidates = new[]
            {
                Path.Combine(contentsPath, "MonoBleedingEdge", "lib", "mono", "msbuild", "Current", "bin", "Roslyn", "csc.exe"),
                Path.Combine(contentsPath, "MonoBleedingEdge", "lib", "mono", "4.5", "csc.exe"),
                Path.Combine(contentsPath, "DotNetSdkRoslyn", "csc.dll")
            };

            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (!File.Exists(candidate))
                {
                    continue;
                }

                if (candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    compiler = new CompilerInvocation
                    {
                        FileName = "dotnet",
                        PrefixArguments = "\"" + candidate + "\""
                    };
                    return true;
                }

                if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
#if UNITY_EDITOR_WIN
                    compiler = new CompilerInvocation
                    {
                        FileName = candidate,
                        PrefixArguments = string.Empty
                    };
#else
                    var monoPath = Path.Combine(contentsPath, "MonoBleedingEdge", "bin", "mono");
                    compiler = new CompilerInvocation
                    {
                        FileName = File.Exists(monoPath) ? monoPath : candidate,
                        PrefixArguments = File.Exists(monoPath) ? "\"" + candidate + "\"" : string.Empty
                    };
#endif
                }
                else
                {
                    compiler = new CompilerInvocation
                    {
                        FileName = candidate,
                        PrefixArguments = string.Empty
                    };
                }

                return true;
            }

            return false;
        }

        private static string RunCompilerProcess(CompilerInvocation compiler, string responsePath, int timeoutMs, List<string> compileErrors)
        {
            var arguments = string.IsNullOrEmpty(compiler.PrefixArguments)
                ? "@\"" + responsePath + "\""
                : compiler.PrefixArguments + " @\"" + responsePath + "\"";

            var outputBuilder = new StringBuilder();
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrEmpty(compiler.FileName) ? FallbackCompilerProcessName : compiler.FileName,
                Arguments = arguments,
                WorkingDirectory = GetProjectRoot(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process())
            {
                process.StartInfo = startInfo;
                var stdoutBuilder = new StringBuilder();
                var stderrBuilder = new StringBuilder();
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        stdoutBuilder.AppendLine(args.Data);
                    }
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        stderrBuilder.AppendLine(args.Data);
                    }
                };

                if (!process.Start())
                {
                    compileErrors.Add("Failed to start C# compiler process.");
                    return string.Empty;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Ignore kill failures.
                    }

                    compileErrors.Add("Compilation timed out after " + timeoutMs + "ms.");
                }
                else
                {
                    process.WaitForExit();
                }

                var stdout = stdoutBuilder.ToString();
                var stderr = stderrBuilder.ToString();
                outputBuilder.Append(stdout);
                outputBuilder.Append(stderr);
                CollectCompilerErrors(stdout, compileErrors);
                CollectCompilerErrors(stderr, compileErrors);
                if (process.HasExited && process.ExitCode != 0 && compileErrors.Count == 0)
                {
                    compileErrors.Add("Compiler exited with code " + process.ExitCode + ".");
                }
            }

            return outputBuilder.ToString();
        }

        private static void CollectCompilerErrors(string output, List<string> compileErrors)
        {
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    compileErrors.Add(line);
                }
            }
        }

        private static InvocationResult InvokeCompiledCode(string assemblyPath, string className)
        {
            var bytes = File.ReadAllBytes(assemblyPath);
            var assembly = Assembly.Load(bytes);
            var type = assembly.GetType(className, true);
            var asyncMethod = type.GetMethod("ExecuteAsync", BindingFlags.Public | BindingFlags.Static);
            if (asyncMethod != null)
            {
                var task = asyncMethod.Invoke(null, null) as Task;
                if (task == null)
                {
                    throw new InvalidOperationException("ExecuteAsync did not return a Task.");
                }

                return new InvocationResult { Task = task };
            }

            var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                throw new MissingMethodException(className, "Execute");
            }

            return new InvocationResult { ReturnValue = method.Invoke(null, null) };
        }

        private static void OnAsyncUpdate()
        {
            if (_activeOperation == null)
            {
                EditorApplication.update -= OnAsyncUpdate;
                return;
            }

            if (!_activeOperation.Step())
            {
                return;
            }

            var result = _activeOperation.BuildResult();
            _activeOperation = null;
            EditorApplication.update -= OnAsyncUpdate;
            WriteResultFile(result);
        }

        private static void WriteResultFile(CommandResult result)
        {
            try
            {
                var resultsDir = Path.Combine(AIBridge.BridgeDirectory, "results");
                if (!Directory.Exists(resultsDir))
                {
                    Directory.CreateDirectory(resultsDir);
                }

                var filePath = Path.Combine(resultsDir, result.id + ".json");
                var json = AIBridgeJson.Serialize(result, true);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AIBridgeLogger.LogError("Failed to write code result for " + result.id + ": " + ex.Message);
            }
        }

        private static object BuildResultData(
            CodeExecutionSession session,
            object returnValue,
            List<string> compileErrors,
            object exception,
            bool? timedOut)
        {
            return new
            {
                enabled = true,
                source = session.Source.Kind,
                sourcePath = session.Source.Path,
                isAsync = session.Source.IsAsync,
                elapsedMs = session.Stopwatch.ElapsedMilliseconds,
                timeoutMs = session.TimeoutMs,
                timedOut = timedOut,
                returnValue = returnValue,
                logs = session.Logs,
                compileErrors = compileErrors ?? new List<string>(),
                compilerOutput = session.CompilerOutput,
                exception = exception
            };
        }

        private static object BuildExceptionInfo(Exception ex)
        {
            var targetInvocation = ex as TargetInvocationException;
            if (targetInvocation != null && targetInvocation.InnerException != null)
            {
                ex = targetInvocation.InnerException;
            }

            var aggregate = ex as AggregateException;
            if (aggregate != null && aggregate.InnerException != null)
            {
                ex = aggregate.InnerException;
            }

            return new
            {
                type = ex.GetType().FullName,
                message = ex.Message,
                stackTrace = ex.StackTrace
            };
        }

        private static object NormalizeReturnValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            var type = value.GetType();
            if (type.IsPrimitive || value is string || value is decimal)
            {
                return value;
            }

            if (value is Enum)
            {
                return value.ToString();
            }

            var unityObject = value as UnityEngine.Object;
            if (unityObject != null)
            {
                return new
                {
                    type = unityObject.GetType().FullName,
                    name = unityObject.name,
                    instanceId = unityObject.GetInstanceID()
                };
            }

            if (value is Vector2 vector2)
            {
                return new { x = vector2.x, y = vector2.y };
            }

            if (value is Vector3 vector3)
            {
                return new { x = vector3.x, y = vector3.y, z = vector3.z };
            }

            if (value is Vector4 vector4)
            {
                return new { x = vector4.x, y = vector4.y, z = vector4.z, w = vector4.w };
            }

            if (value is Quaternion quaternion)
            {
                return new { x = quaternion.x, y = quaternion.y, z = quaternion.z, w = quaternion.w };
            }

            if (value is Color color)
            {
                return new { r = color.r, g = color.g, b = color.b, a = color.a };
            }

            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                return dictionary;
            }

            if (value is IEnumerable && !(value is string))
            {
                return value;
            }

            return new
            {
                type = type.FullName,
                value = value.ToString()
            };
        }

        private static CommandResult FailureWithData(string requestId, string error, object data)
        {
            return new CommandResult
            {
                id = requestId,
                success = false,
                error = error,
                data = data
            };
        }

        private static object BuildSettingsGateData(AIBridgeProjectSettings settings)
        {
            return new
            {
                enabled = settings.EnableCodeExecution,
                riskAccepted = settings.CodeExecutionRiskAccepted,
                source = "none"
            };
        }

        private static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }

        private static string ResolveCodeFilePath(string file)
        {
            if (Path.IsPathRooted(file))
            {
                return Path.GetFullPath(file);
            }

            // Unity Editor 的当前工作目录在不同启动方式下不稳定，文件来源统一按项目根目录解析。
            return Path.GetFullPath(Path.Combine(GetProjectRoot(), file));
        }

        private static bool IsSameOrChildPath(string rootDirectory, string fullPath)
        {
            var normalizedRoot = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class SourceText
        {
            public string Kind;
            public string Path;
            public string Code;
            public string WrappedCode;
            public string ClassName;
            public bool IsAsync;
        }

        private sealed class CompilerInvocation
        {
            public string FileName;
            public string PrefixArguments;
        }

        private sealed class InvocationResult
        {
            public object ReturnValue;
            public Task Task;
        }

        [Serializable]
        private sealed class CapturedLog
        {
            public string type;
            public string message;
            public string stackTrace;
        }

        private sealed class CodeExecutionSession
        {
            public CodeExecutionSession(string requestId, SourceText source, int timeoutMs, Stopwatch stopwatch)
            {
                RequestId = requestId;
                Source = source;
                TimeoutMs = timeoutMs;
                Stopwatch = stopwatch;
                Logs = new List<CapturedLog>();
            }

            public string RequestId;
            public SourceText Source;
            public int TimeoutMs;
            public Stopwatch Stopwatch;
            public List<CapturedLog> Logs;
            public string CompiledAssemblyPath;
            public string CompilerOutput;

            public void OnLogMessageReceived(string condition, string stackTrace, LogType type)
            {
                Logs.Add(new CapturedLog
                {
                    type = type.ToString(),
                    message = condition,
                    stackTrace = stackTrace
                });
            }
        }

        private sealed class CodeAsyncOperation
        {
            private readonly CodeExecutionSession _session;
            private readonly Task _task;
            private CommandResult _result;

            public CodeAsyncOperation(CodeExecutionSession session, Task task)
            {
                _session = session;
                _task = task;
            }

            public bool Step()
            {
                if (_task.IsCompleted)
                {
                    Application.logMessageReceived -= _session.OnLogMessageReceived;
                    _session.Stopwatch.Stop();

                    if (_task.IsFaulted)
                    {
                        _result = FailureWithData(
                            _session.RequestId,
                            "Code execution failed.",
                            BuildResultData(_session, null, new List<string>(), BuildExceptionInfo(_task.Exception), false));
                    }
                    else if (_task.IsCanceled)
                    {
                        _result = FailureWithData(
                            _session.RequestId,
                            "Code execution was canceled.",
                            BuildResultData(_session, null, new List<string>(), null, false));
                    }
                    else
                    {
                        _result = CommandResult.Success(
                            _session.RequestId,
                            BuildResultData(_session, NormalizeReturnValue(GetTaskResult(_task)), new List<string>(), null, false));
                    }

                    return true;
                }

                if (_session.Stopwatch.ElapsedMilliseconds >= _session.TimeoutMs)
                {
                    Application.logMessageReceived -= _session.OnLogMessageReceived;
                    _session.Stopwatch.Stop();
                    _result = FailureWithData(
                        _session.RequestId,
                        "Code execution timed out after " + _session.TimeoutMs + "ms.",
                        BuildResultData(_session, null, new List<string>(), null, true));
                    return true;
                }

                return false;
            }

            public CommandResult BuildResult()
            {
                return _result ?? CommandResult.Failure(_session.RequestId, "Code execution ended without a result.");
            }

            private static object GetTaskResult(Task task)
            {
                var type = task.GetType();
                if (!type.IsGenericType)
                {
                    return null;
                }

                var resultProperty = type.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                return resultProperty != null ? resultProperty.GetValue(task, null) : null;
            }
        }
    }
}
