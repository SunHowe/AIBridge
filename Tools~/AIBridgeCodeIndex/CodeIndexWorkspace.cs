using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json;

namespace AIBridgeCodeIndex
{
    internal sealed class CodeIndexWorkspace
    {
        private const int MaxSymbolResults = 100;
        private const int MaxReferenceResults = 500;
        private const int MaxDiagnosticResults = 500;

        private readonly string _projectRoot;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _manifestLock = new object();
        private MSBuildWorkspace _workspace;
        private Solution _solution;
        private List<CodeIndexItem> _symbols = new List<CodeIndexItem>();
        private List<string> _workspaceWarnings = new List<string>();
        private List<ManifestEntry> _manifest = new List<ManifestEntry>();

        public CodeIndexWorkspace(string projectRoot, string solutionPath)
        {
            _projectRoot = Path.GetFullPath(projectRoot);
            SolutionPath = ResolveSolutionPath(_projectRoot, solutionPath);
        }

        public string SolutionPath { get; private set; }
        public int LoadedProjects { get; private set; }
        public int LoadedDocuments { get; private set; }

        public bool IsStale()
        {
            lock (_manifestLock)
            {
                if (_manifest == null || _manifest.Count == 0)
                {
                    return true;
                }

                return !AreManifestsEqual(_manifest, BuildManifest());
            }
        }

        public async Task WarmupAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(SolutionPath) || !File.Exists(SolutionPath))
                {
                    throw new FileNotFoundException("Unity solution file was not found under project root.", SolutionPath);
                }

                var properties = new Dictionary<string, string>
                {
                    { "Configuration", "Debug" }
                };

                if (_workspace != null)
                {
                    _workspace.Dispose();
                }

                _workspaceWarnings = new List<string>();
                _workspace = MSBuildWorkspace.Create(properties);
                _workspace.WorkspaceFailed += (sender, args) =>
                {
                    if (args != null && args.Diagnostic != null)
                    {
                        _workspaceWarnings.Add(args.Diagnostic.Message);
                    }
                };

                _solution = await _workspace.OpenSolutionAsync(SolutionPath);
                LoadedProjects = _solution.Projects.Count();
                LoadedDocuments = _solution.Projects.SelectMany(project => project.Documents).Count(document => IsCSharpDocument(document));
                _symbols = await BuildSymbolTableAsync(_solution);
                lock (_manifestLock)
                {
                    _manifest = BuildManifest();
                }

                WriteManifestFile();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<CodeIndexResponse> QueryAsync(string action, Dictionary<string, object> parameters)
        {
            await _gate.WaitAsync();
            try
            {
                switch ((action ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "symbol":
                        return QuerySymbol(GetString(parameters, "query"));
                    case "definition":
                        return await QueryDefinitionAsync(parameters);
                    case "references":
                        return await QueryReferencesAsync(parameters);
                    case "implementations":
                        return await QueryImplementationsAsync(parameters);
                    case "derived":
                        return await QueryDerivedAsync(parameters);
                    case "callers":
                        return await QueryCallersAsync(parameters);
                    case "diagnostics":
                        return await QueryDiagnosticsAsync(parameters);
                    default:
                        return new CodeIndexResponse
                        {
                            success = false,
                            semantic = true,
                            source = "roslyn-msbuild",
                            state = "ready",
                            stale = false,
                            projectRoot = _projectRoot,
                            solution = SolutionPath,
                            loadedProjects = LoadedProjects,
                            loadedDocuments = LoadedDocuments,
                            error = "Unsupported code_index action: " + action
                        };
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private CodeIndexResponse QuerySymbol(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Missing required parameter: --query");
            }

            var normalized = query.Trim();
            var items = _symbols
                .Where(item => Contains(item.name, normalized)
                               || Contains(item.container, normalized)
                               || Contains(item.signature, normalized))
                .OrderBy(item => ScoreSymbol(item, normalized))
                .ThenBy(item => item.file, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.line)
                .Take(MaxSymbolResults)
                .ToList();

            return new CodeIndexResponse
            {
                items = items,
                warning = BuildWorkspaceWarning()
            };
        }

        private async Task<CodeIndexResponse> QueryDefinitionAsync(Dictionary<string, object> parameters)
        {
            var document = ResolveDocument(parameters, out var sourceText, out var position);
            var semanticModel = await document.GetSemanticModelAsync();
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);
            var items = new List<CodeIndexItem>();

            if (symbol != null)
            {
                foreach (var location in symbol.Locations)
                {
                    AddLocationItem(items, symbol, location);
                }
            }

            return new CodeIndexResponse
            {
                items = items,
                warning = BuildWorkspaceWarning()
            };
        }

        private async Task<CodeIndexResponse> QueryReferencesAsync(Dictionary<string, object> parameters)
        {
            var document = ResolveDocument(parameters, out var sourceText, out var position);
            var semanticModel = await document.GetSemanticModelAsync();
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);
            var items = new List<CodeIndexItem>();

            if (symbol != null)
            {
                var references = await SymbolFinder.FindReferencesAsync(symbol, _solution);
                foreach (var referencedSymbol in references)
                {
                    foreach (var reference in referencedSymbol.Locations)
                    {
                        AddLocationItem(items, referencedSymbol.Definition, reference.Location);
                    }
                }
            }

            return new CodeIndexResponse
            {
                items = items
                    .OrderBy(item => item.file, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.line)
                    .ThenBy(item => item.column)
                    .Take(MaxReferenceResults)
                    .ToList(),
                warning = BuildWorkspaceWarning()
            };
        }

        private async Task<CodeIndexResponse> QueryImplementationsAsync(Dictionary<string, object> parameters)
        {
            var type = await ResolveTypeSymbolAsync(GetString(parameters, "type"));
            var items = new List<CodeIndexItem>();

            if (type != null)
            {
                var implementations = await SymbolFinder.FindImplementationsAsync(type, _solution);
                foreach (var implementation in implementations)
                {
                    AddSourceLocations(items, implementation);
                }
            }

            return new CodeIndexResponse
            {
                items = DistinctItems(items)
                    .OrderBy(item => item.file, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.line)
                    .Take(MaxReferenceResults)
                    .ToList(),
                warning = type == null ? "Type was not found in the loaded solution." : BuildWorkspaceWarning()
            };
        }

        private async Task<CodeIndexResponse> QueryDerivedAsync(Dictionary<string, object> parameters)
        {
            var type = await ResolveTypeSymbolAsync(GetString(parameters, "type"));
            var items = new List<CodeIndexItem>();

            if (type != null)
            {
                if (type.TypeKind == TypeKind.Class)
                {
                    var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(type, _solution);
                    foreach (var derivedClass in derivedClasses)
                    {
                        AddSourceLocations(items, derivedClass);
                    }
                }

                if (type.TypeKind == TypeKind.Interface)
                {
                    var derivedInterfaces = await SymbolFinder.FindDerivedInterfacesAsync(type, _solution);
                    foreach (var derivedInterface in derivedInterfaces)
                    {
                        AddSourceLocations(items, derivedInterface);
                    }

                    var implementations = await SymbolFinder.FindImplementationsAsync(type, _solution);
                    foreach (var implementation in implementations)
                    {
                        AddSourceLocations(items, implementation);
                    }
                }
            }

            return new CodeIndexResponse
            {
                items = DistinctItems(items)
                    .OrderBy(item => item.file, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.line)
                    .Take(MaxReferenceResults)
                    .ToList(),
                warning = type == null ? "Type was not found in the loaded solution." : BuildWorkspaceWarning()
            };
        }

        private async Task<CodeIndexResponse> QueryCallersAsync(Dictionary<string, object> parameters)
        {
            var document = ResolveDocument(parameters, out var sourceText, out var position);
            var semanticModel = await document.GetSemanticModelAsync();
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);
            var items = new List<CodeIndexItem>();

            if (symbol != null)
            {
                var callers = await SymbolFinder.FindCallersAsync(symbol, _solution);
                foreach (var caller in callers)
                {
                    foreach (var location in caller.Locations)
                    {
                        AddLocationItem(items, caller.CallingSymbol, location);
                    }
                }
            }

            return new CodeIndexResponse
            {
                items = DistinctItems(items)
                    .OrderBy(item => item.file, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.line)
                    .ThenBy(item => item.column)
                    .Take(MaxReferenceResults)
                    .ToList(),
                warning = BuildWorkspaceWarning()
            };
        }

        private async Task<CodeIndexResponse> QueryDiagnosticsAsync(Dictionary<string, object> parameters)
        {
            var file = GetString(parameters, "file");
            var diagnostics = new List<Diagnostic>();

            if (!string.IsNullOrWhiteSpace(file))
            {
                var document = ResolveDocument(file);
                var semanticModel = await document.GetSemanticModelAsync();
                diagnostics.AddRange(semanticModel.GetDiagnostics());
            }
            else
            {
                foreach (var project in _solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation != null)
                    {
                        diagnostics.AddRange(compilation.GetDiagnostics());
                    }
                }
            }

            return new CodeIndexResponse
            {
                items = diagnostics
                    .Where(diagnostic => diagnostic != null)
                    .OrderByDescending(diagnostic => diagnostic.Severity)
                    .ThenBy(diagnostic => diagnostic.Location == null ? string.Empty : diagnostic.Location.GetLineSpan().Path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(diagnostic => diagnostic.Location == null ? 0 : diagnostic.Location.GetLineSpan().StartLinePosition.Line)
                    .Take(MaxDiagnosticResults)
                    .Select(ToDiagnosticItem)
                    .ToList(),
                warning = BuildWorkspaceWarning()
            };
        }

        private Document ResolveDocument(Dictionary<string, object> parameters, out SourceText sourceText, out int position)
        {
            var file = GetString(parameters, "file");
            var line = GetInt(parameters, "line");
            var column = GetInt(parameters, "column");

            if (string.IsNullOrWhiteSpace(file))
            {
                throw new ArgumentException("Missing required parameter: --file");
            }

            if (line <= 0 || column <= 0)
            {
                throw new ArgumentException("--line and --column must be positive 1-based numbers.");
            }

            var fullPath = Path.IsPathRooted(file)
                ? Path.GetFullPath(file)
                : Path.GetFullPath(Path.Combine(_projectRoot, file));
            var document = _solution.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(item => string.Equals(Path.GetFullPath(item.FilePath ?? string.Empty), fullPath, StringComparison.OrdinalIgnoreCase));
            if (document == null)
            {
                throw new FileNotFoundException("File is not part of the loaded Roslyn solution.", file);
            }

            sourceText = document.GetTextAsync().GetAwaiter().GetResult();
            if (line > sourceText.Lines.Count)
            {
                throw new ArgumentOutOfRangeException("line", "Line is outside the document.");
            }

            var textLine = sourceText.Lines[line - 1];
            var zeroBasedColumn = Math.Max(0, column - 1);
            var offsetInLine = Math.Min(zeroBasedColumn, Math.Max(0, textLine.End - textLine.Start));
            position = textLine.Start + offsetInLine;
            return document;
        }

        private Document ResolveDocument(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                throw new ArgumentException("Missing required parameter: --file");
            }

            var fullPath = Path.IsPathRooted(file)
                ? Path.GetFullPath(file)
                : Path.GetFullPath(Path.Combine(_projectRoot, file));
            var document = _solution.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(item => string.Equals(Path.GetFullPath(item.FilePath ?? string.Empty), fullPath, StringComparison.OrdinalIgnoreCase));
            if (document == null)
            {
                throw new FileNotFoundException("File is not part of the loaded Roslyn solution.", file);
            }

            return document;
        }

        private async Task<INamedTypeSymbol> ResolveTypeSymbolAsync(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new ArgumentException("Missing required parameter: --type");
            }

            var normalized = typeName.Trim();
            foreach (var project in _solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                {
                    continue;
                }

                var metadataType = compilation.GetTypeByMetadataName(normalized);
                if (metadataType != null)
                {
                    return metadataType;
                }

                var found = FindTypeInNamespace(compilation.GlobalNamespace, normalized);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static INamedTypeSymbol FindTypeInNamespace(INamespaceSymbol namespaceSymbol, string query)
        {
            if (namespaceSymbol == null)
            {
                return null;
            }

            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                var found = FindTypeInType(type, query);
                if (found != null)
                {
                    return found;
                }
            }

            foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                var found = FindTypeInNamespace(childNamespace, query);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static INamedTypeSymbol FindTypeInType(INamedTypeSymbol type, string query)
        {
            if (type == null)
            {
                return null;
            }

            if (string.Equals(type.Name, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.ToDisplayString(), query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), query, StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }

            foreach (var nestedType in type.GetTypeMembers())
            {
                var found = FindTypeInType(nestedType, query);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void AddSourceLocations(List<CodeIndexItem> items, ISymbol symbol)
        {
            if (symbol == null)
            {
                return;
            }

            foreach (var location in symbol.Locations)
            {
                AddLocationItem(items, symbol, location);
            }
        }

        private static IEnumerable<CodeIndexItem> DistinctItems(IEnumerable<CodeIndexItem> items)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var key = (item.kind ?? string.Empty)
                          + "|" + (item.name ?? string.Empty)
                          + "|" + (item.file ?? string.Empty)
                          + "|" + item.line
                          + "|" + item.column;
                if (seen.Add(key))
                {
                    yield return item;
                }
            }
        }

        private CodeIndexItem ToDiagnosticItem(Diagnostic diagnostic)
        {
            var span = diagnostic.Location == null ? default(FileLinePositionSpan) : diagnostic.Location.GetLineSpan();
            var hasPath = diagnostic.Location != null
                          && diagnostic.Location.IsInSource
                          && !string.IsNullOrEmpty(span.Path);

            return new CodeIndexItem
            {
                kind = "diagnostic",
                name = diagnostic.Id,
                id = diagnostic.Id,
                severity = diagnostic.Severity.ToString(),
                message = diagnostic.GetMessage(),
                file = hasPath ? ToProjectRelativePath(span.Path) : null,
                line = hasPath ? span.StartLinePosition.Line + 1 : 0,
                column = hasPath ? span.StartLinePosition.Character + 1 : 0,
                preview = diagnostic.GetMessage()
            };
        }

        private async Task<List<CodeIndexItem>> BuildSymbolTableAsync(Solution solution)
        {
            var result = new List<CodeIndexItem>();
            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (!IsCSharpDocument(document))
                    {
                        continue;
                    }

                    var root = await document.GetSyntaxRootAsync();
                    var semanticModel = await document.GetSemanticModelAsync();
                    if (root == null || semanticModel == null)
                    {
                        continue;
                    }

                    foreach (var node in root.DescendantNodes())
                    {
                        ISymbol symbol = null;
                        if (node is BaseTypeDeclarationSyntax
                            || node is DelegateDeclarationSyntax
                            || node is MethodDeclarationSyntax
                            || node is ConstructorDeclarationSyntax
                            || node is PropertyDeclarationSyntax)
                        {
                            symbol = semanticModel.GetDeclaredSymbol(node);
                        }
                        else if (node is VariableDeclaratorSyntax variable
                                 && (variable.Parent != null && variable.Parent.Parent is FieldDeclarationSyntax))
                        {
                            symbol = semanticModel.GetDeclaredSymbol(variable);
                        }

                        if (symbol == null)
                        {
                            continue;
                        }

                        var location = symbol.Locations.FirstOrDefault(item => item.IsInSource);
                        if (location != null)
                        {
                            AddLocationItem(result, symbol, location);
                        }
                    }
                }
            }

            return result;
        }

        private void AddLocationItem(List<CodeIndexItem> items, ISymbol symbol, Location location)
        {
            if (symbol == null || location == null || !location.IsInSource)
            {
                return;
            }

            var span = location.GetLineSpan();
            var filePath = string.IsNullOrEmpty(span.Path) ? null : ToProjectRelativePath(span.Path);
            items.Add(new CodeIndexItem
            {
                kind = symbol.Kind.ToString().ToLowerInvariant(),
                name = symbol.Name,
                container = GetContainer(symbol),
                file = filePath,
                line = span.StartLinePosition.Line + 1,
                column = span.StartLinePosition.Character + 1,
                signature = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            });
        }

        private string ToProjectRelativePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var root = _projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(root.Length).Replace('\\', '/');
            }

            return fullPath.Replace('\\', '/');
        }

        private static string GetContainer(ISymbol symbol)
        {
            if (symbol == null)
            {
                return null;
            }

            if (symbol.ContainingType != null)
            {
                return symbol.ContainingType.ToDisplayString();
            }

            if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
            {
                return symbol.ContainingNamespace.ToDisplayString();
            }

            return null;
        }

        private static bool IsCSharpDocument(Document document)
        {
            return document != null
                && !string.IsNullOrEmpty(document.FilePath)
                && document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSolutionPath(string projectRoot, string explicitSolutionPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitSolutionPath))
            {
                return Path.IsPathRooted(explicitSolutionPath)
                    ? Path.GetFullPath(explicitSolutionPath)
                    : Path.GetFullPath(Path.Combine(projectRoot, explicitSolutionPath));
            }

            var solutions = Directory.GetFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly);
            if (solutions.Length == 0)
            {
                return null;
            }

            var projectName = new DirectoryInfo(projectRoot).Name;
            var matching = solutions.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), projectName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(matching))
            {
                return matching;
            }

            return solutions
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();
        }

        private List<ManifestEntry> BuildManifest()
        {
            var entries = new List<ManifestEntry>();
            AddManifestFile(entries, SolutionPath);
            AddManifestFiles(entries, _projectRoot, "*.cs", SearchOption.AllDirectories);
            AddManifestFiles(entries, _projectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
            AddManifestFiles(entries, _projectRoot, "*.asmdef", SearchOption.AllDirectories);
            AddManifestFile(entries, Path.Combine(_projectRoot, "Packages", "manifest.json"));
            AddManifestFile(entries, Path.Combine(_projectRoot, "Packages", "packages-lock.json"));
            return entries
                .Where(entry => entry != null)
                .OrderBy(entry => entry.path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void WriteManifestFile()
        {
            try
            {
                var directory = Path.Combine(_projectRoot, ".aibridge", "code-index");
                Directory.CreateDirectory(directory);
                List<ManifestEntry> snapshot;
                lock (_manifestLock)
                {
                    snapshot = _manifest == null ? new List<ManifestEntry>() : _manifest.ToList();
                }

                File.WriteAllText(
                    Path.Combine(directory, "manifest.json"),
                    JsonConvert.SerializeObject(snapshot, Formatting.Indented),
                    System.Text.Encoding.UTF8);
            }
            catch
            {
                // manifest 是可删除缓存，写入失败不影响语义查询。
            }
        }

        private static void AddManifestFile(List<ManifestEntry> entries, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            var file = new FileInfo(path);
            entries.Add(new ManifestEntry
            {
                path = path.Replace('\\', '/'),
                ticks = file.LastWriteTimeUtc.Ticks,
                length = file.Length
            });
        }

        private void AddManifestFiles(List<ManifestEntry> entries, string root, string pattern, SearchOption searchOption)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return;
            }

            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                string[] files;
                try
                {
                    files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var path in files)
                {
                    if (ShouldSkipManifestFile(path))
                    {
                        continue;
                    }

                    AddManifestFile(entries, path);
                }

                if (searchOption != SearchOption.AllDirectories)
                {
                    continue;
                }

                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(directory);
                }
                catch
                {
                    continue;
                }

                foreach (var childDirectory in directories)
                {
                    if (!ShouldSkipManifestDirectory(childDirectory))
                    {
                        pending.Push(childDirectory);
                    }
                }
            }
        }

        private bool ShouldSkipManifestFile(string path)
        {
            var relative = ToProjectRelativePath(path);
            return relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(".aibridge/code-index/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("Library/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("Temp/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldSkipManifestDirectory(string path)
        {
            var name = Path.GetFileName(path);
            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Library", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Temp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var relative = ToProjectRelativePath(path).TrimEnd('/') + "/";
            return relative.StartsWith(".aibridge/code-index/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool AreManifestsEqual(List<ManifestEntry> left, List<ManifestEntry> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                var a = left[i];
                var b = right[i];
                if (a == null || b == null
                    || !string.Equals(a.path, b.path, StringComparison.OrdinalIgnoreCase)
                    || a.ticks != b.ticks
                    || a.length != b.length)
                {
                    return false;
                }
            }

            return true;
        }

        private string BuildWorkspaceWarning()
        {
            if (_workspaceWarnings == null || _workspaceWarnings.Count == 0)
            {
                return null;
            }

            return string.Join(" | ", _workspaceWarnings.Take(5));
        }

        private static int ScoreSymbol(CodeIndexItem item, string query)
        {
            if (string.Equals(item.name, query, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (!string.IsNullOrEmpty(item.name) && item.name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (Contains(item.container, query))
            {
                return 2;
            }

            return 3;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetString(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return Convert.ToString(value);
        }

        private static int GetInt(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value) || value == null)
            {
                return 0;
            }

            if (value is long longValue)
            {
                return (int)longValue;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            int.TryParse(Convert.ToString(value), out var result);
            return result;
        }

        private sealed class ManifestEntry
        {
            public string path { get; set; }
            public long ticks { get; set; }
            public long length { get; set; }
        }
    }
}
