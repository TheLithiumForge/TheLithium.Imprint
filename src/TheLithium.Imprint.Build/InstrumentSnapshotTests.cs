using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TheLithium.Imprint.Build;

/// <summary>
/// Supplies the test-body lifetime that an additive source generator cannot introduce.
/// The accepted normal-flow API requires completion after the body and its finally blocks.
/// Original source files are read only; replacement compile inputs live in IntermediateOutputPath.
/// </summary>
public sealed class InstrumentSnapshotTests : Task
{
    [Required]
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public ITaskItem[] References { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] ReplacedSources { get; private set; } = Array.Empty<ITaskItem>();

    [Output]
    public ITaskItem[] InstrumentedSources { get; private set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            var defines = DefineConstants.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: defines);
            var trees = Sources.Select(source => CSharpSyntaxTree.ParseText(
                File.ReadAllText(source.ItemSpec), parseOptions, Path.GetFullPath(source.ItemSpec))).ToArray();
            var references = References.Select(reference => MetadataReference.CreateFromFile(reference.ItemSpec));
            var compilation = CSharpCompilation.Create("Imprint.Instrumentation", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var lifetimes = SnapshotCallGraph.FindLifetimes(compilation);
            var replaced = new List<ITaskItem>();
            var outputs = new List<ITaskItem>();
            Directory.CreateDirectory(OutputDirectory);

            for (var index = 0; index < trees.Length; index++)
            {
                if (string.Equals(Sources[index].GetMetadata("ImprintInstrument"), "false", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var tree = trees[index];
                var model = compilation.GetSemanticModel(tree);
                var edits = new List<TextChange>();
                foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!(model.GetDeclaredSymbol(method) is IMethodSymbol symbol) || !lifetimes.Contains(symbol.OriginalDefinition))
                    {
                        continue;
                    }

                    if (method.ParameterList.Parameters.Any(parameter => parameter.Modifiers.Any(
                        modifier => modifier.IsKind(SyntaxKind.RefKeyword) || modifier.IsKind(SyntaxKind.OutKeyword)
                            || modifier.IsKind(SyntaxKind.InKeyword)))
                        || method.DescendantNodes().OfType<YieldStatementSyntax>().Any())
                    {
                        ReportUnsupported(tree, method, "Iterator and ref/in/out test methods cannot have an automatic snapshot lifetime.");
                        continue;
                    }

                    if (symbol.ReturnsByRef || symbol.ReturnsByRefReadonly || symbol.ContainingType.IsValueType)
                    {
                        ReportUnsupported(tree, method, "Snapshot test methods must be declared in a class and cannot return by reference.");
                        continue;
                    }

                    var asyncModifier = method.Modifiers.FirstOrDefault(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword));
                    if (asyncModifier.RawKind != 0 && symbol.ReturnsVoid)
                    {
                        ReportUnsupported(tree, method, "An async snapshot test must return Task or ValueTask, never async void.");
                        continue;
                    }

                    if (asyncModifier.RawKind != 0)
                    {
                        edits.Add(new TextChange(asyncModifier.Span, new string(' ', asyncModifier.Span.Length)));
                    }

                    var originalBody = (SyntaxNode?)method.Body ?? method.ExpressionBody;
                    if (originalBody == null)
                    {
                        continue;
                    }
                    var bodySpan = TextSpan.FromBounds(originalBody.SpanStart,
                        method.Body?.Span.End ?? method.SemicolonToken.Span.End);
                    edits.Add(new TextChange(bodySpan, BuildLifetime(method, symbol, model)));
                }

                if (edits.Count == 0)
                {
                    continue;
                }

                var instrumented = tree.GetText().WithChanges(edits);
                var fileName = $"{index:D4}-{Path.GetFileName(tree.FilePath)}";
                var output = Path.Combine(OutputDirectory, fileName);
                var sourceFile = NormalizeFilePath(tree.FilePath);
                var content = $"#line 1 {LineDirectiveLiteral(sourceFile)}\n{instrumented}";
                if (!File.Exists(output) || File.ReadAllText(output) != content)
                {
                    File.WriteAllText(output, content, new UTF8Encoding(false));
                }

                var outputItem = new TaskItem(output, Sources[index].CloneCustomMetadata());
                outputItem.SetMetadata("ImprintOriginalSource", tree.FilePath);
                replaced.Add(Sources[index]);
                outputs.Add(outputItem);
            }

            ReplacedSources = replaced.ToArray();
            InstrumentedSources = outputs.ToArray();
            return !Log.HasLoggedErrors;
        }
        catch (Exception error)
        {
            Log.LogErrorFromException(error, showStackTrace: true);
            return false;
        }
    }

    private static string BuildLifetime(MethodDeclarationSyntax method, IMethodSymbol symbol, SemanticModel model)
    {
        var source = NormalizeFilePath(method.SyntaxTree.FilePath);
        var sourceLiteral = CodeLiteral(source);
        var sourceDirective = LineDirectiveLiteral(source);
        var methodLine = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody
            ?? throw new InvalidOperationException("A lifetime requires a method body.");
        var bodyLine = body.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var endLine = method.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        var functionName = $"__ImprintBody_{method.SpanStart}";
        var functionAsync = method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)) ? "async " : string.Empty;
        var original = CallerInformation.Preserve(body, model);
        if (body is ArrowExpressionClauseSyntax expressionBody)
        {
            var expression = original.Substring(expressionBody.ArrowToken.Span.End - body.SpanStart);
            var returnsNothing = symbol.ReturnsVoid || (functionAsync.Length != 0
                && symbol.ReturnType is INamedTypeSymbol task && task.TypeArguments.Length == 0);
            original = returnsNothing ? $"{{ {expression}; }}" : $"{{ return {expression}; }}";
        }

        var runner = "Run";
        if (symbol.ReturnType is INamedTypeSymbol named && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
        {
            runner = named.Name == "ValueTask" ? "RunValueTask" : "RunAsync";
        }

        var parameters = symbol.Parameters.Where(parameter => parameter.Type.ToDisplayString() != "System.Threading.CancellationToken").ToArray();
        var options = "new global::TheLithium.Imprint.SnapshotTestOptions()";
        if (parameters.Length != 0)
        {
            var fields = string.Join(", ", parameters.Select(parameter => $"@{parameter.Name}"));
            options = $"new global::TheLithium.Imprint.SnapshotTestOptions {{ Case = global::TheLithium.Imprint.Generation.SnapshotCases.Create(new {{ {fields} }}) }}";
        }

        var prefix = symbol.ReturnsVoid ? string.Empty : "return ";
        return $$"""
            {
            #line hidden
                    {{prefix}}global::TheLithium.Imprint.Generation.TestExecution.{{runner}}(
                    {{functionName}}, {{options}}, sourceFile: {{sourceLiteral}}, sourceLine: {{methodLine}});

                {{functionAsync}}{{method.ReturnType}} {{functionName}}()
            #line {{bodyLine}} {{sourceDirective}}
                {{original}}
            #line hidden
            }
            #line {{endLine}} {{sourceDirective}}
            """;
    }

    private void ReportUnsupported(SyntaxTree tree, MethodDeclarationSyntax method, string message)
    {
        var position = method.GetLocation().GetLineSpan().StartLinePosition;
        Log.LogError(null, "IMP102", null, tree.FilePath, position.Line + 1, position.Character + 1, 0, 0, message);
    }

    private static string NormalizeFilePath(string text) => text.Replace('\\', '/');

    private static string CodeLiteral(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    private static string LineDirectiveLiteral(string text) => $"\"{text.Replace("\"", "\\\"")}\"";
}
