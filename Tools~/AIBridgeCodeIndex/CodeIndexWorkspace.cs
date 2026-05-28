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

namespace AIBridgeCodeIndex
{
    internal sealed class CodeIndexWorkspace
    {
        private readonly string _projectRoot;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private MSBuildWorkspace _workspace;
        private Solution _solution;
        private List<CodeIndexItem> _symbols = new List<CodeIndexItem>();
        private List<string> _workspaceWarnings = new List<string>();

        public CodeIndexWorkspace(string projectRoot, string solutionPath)
        {
            _projectRoot = Path.GetFullPath(projectRoot);
            SolutionPath = ResolveSolutionPath(_projectRoot, solutionPath);
        }

        public string SolutionPath { get; private set; }
        public int LoadedProjects { get; private set; }
        public int LoadedDocuments { get; private set; }

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
                .Take(100)
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
                    .Take(500)
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
            if (location == null || !location.IsInSource)
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
    }
}
