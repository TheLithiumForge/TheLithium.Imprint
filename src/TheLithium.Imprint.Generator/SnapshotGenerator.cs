using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TheLithium.Imprint.Build;

namespace TheLithium.Imprint.Generator;

[Generator(LanguageNames.CSharp)]
public sealed partial class SnapshotGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Unsupported = new DiagnosticDescriptor(
        "IMP001", "Snapshot type needs an explicit writer",
        "Cannot generate a reflection-free writer for '{0}': {1}. Supply an explicit writer or project the value.",
        "TheLithium.Imprint", DiagnosticSeverity.Warning, true);

    private sealed class Candidate
    {
        internal readonly InvocationExpressionSyntax Syntax;
        internal readonly ITypeSymbol? Root;
        internal readonly MethodDeclarationSyntax? Declaration;
        internal readonly IMethodSymbol? Caller;
        internal readonly bool IsBoundary;
        internal string? OriginalFile
        {
            get; set;
        }
        internal int? OriginalLine
        {
            get; set;
        }
        internal Candidate(InvocationExpressionSyntax syntax, ITypeSymbol? root,
            MethodDeclarationSyntax? declaration, IMethodSymbol? caller, bool boundary)
        {
            Syntax = syntax;
            Root = root;
            Declaration = declaration;
            Caller = caller;
            IsBoundary = boundary;
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var calls = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => IsCandidate(node),
            static (ctx, token) => Inspect(ctx, token)).Where(static x => x != null).Collect();
        var inputs = calls.Combine(context.CompilationProvider).Combine(context.AnalyzerConfigOptionsProvider);
        context.RegisterSourceOutput(inputs, static (output, input) =>
        {
            var candidates = input.Left.Left;
            var compilation = input.Left.Right;
            input.Right.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var project);
            input.Right.GlobalOptions.TryGetValue("build_property.MSBuildProjectName", out var projectName);
            input.Right.GlobalOptions.TryGetValue("build_property.ArtifactsPath", out var artifacts);
            Generate(output, compilation, candidates, project ?? "", projectName ?? compilation.AssemblyName ?? "Tests", artifacts);
        });
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (!(node is InvocationExpressionSyntax invocation))
        {
            return false;
        }

        var name = Name(invocation.Expression);
        return name == "AssertSnapshot" || name == "UpdateSnapshot" || name == "Create"
            || name == "Run" || name == "RunAsync" || name == "RunValueTask" || name == "Begin" || name == "Write";
    }

    private static string Name(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax member)
        {
            return member.Name.Identifier.ValueText;
        }

        if (expression is MemberBindingExpressionSyntax binding)
        {
            return binding.Name.Identifier.ValueText;
        }

        if (expression is SimpleNameSyntax simple)
        {
            return simple.Identifier.ValueText;
        }

        return "";
    }

    private static Candidate? Inspect(GeneratorSyntaxContext context, CancellationToken token)
    {
        var syntax = (InvocationExpressionSyntax)context.Node;
        if (!(context.SemanticModel.GetSymbolInfo(syntax, token).Symbol is IMethodSymbol method))
        {
            return null;
        }

        var definition = method.ReducedFrom ?? method;
        var owner = definition.ContainingType.ToDisplayString();
        var boundary = owner == "TheLithium.Imprint.Snapshots" && (method.Name == "Run" || method.Name == "RunAsync" || method.Name == "Begin");
        boundary |= owner == "TheLithium.Imprint.Generation.TestExecution";
        var capture = owner == "TheLithium.Imprint.SnapshotExtensions" && (method.Name == "AssertSnapshot" || method.Name == "UpdateSnapshot");
        var explicitWrite = owner == "TheLithium.Imprint.SnapshotWriters" && method.Name == "Write";
        explicitWrite |= owner == "TheLithium.Imprint.Generation.SnapshotCases" && method.Name == "Create";
        if (!boundary && !capture && !explicitWrite)
        {
            return null;
        }

        var declaration = syntax.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var caller = declaration == null ? null : context.SemanticModel.GetDeclaredSymbol(declaration, token);
        ITypeSymbol? root = null;
        if ((capture || explicitWrite) && method.TypeArguments.Length == 1
            && !definition.Parameters.Any(p => p.Name == "writer" && capture))
        {
            root = method.TypeArguments[0];
        }

        var candidate = new Candidate(syntax, root, declaration, caller, boundary);
        if (owner == "TheLithium.Imprint.Generation.TestExecution")
        {
            foreach (var argument in syntax.ArgumentList.Arguments)
            {
                var value = context.SemanticModel.GetConstantValue(argument.Expression, token);
                if (argument.NameColon?.Name.Identifier.ValueText == "sourceFile" && value.Value is string originalFile)
                {
                    candidate.OriginalFile = originalFile;
                }
                if (argument.NameColon?.Name.Identifier.ValueText == "sourceLine" && value.Value is int originalLine)
                {
                    candidate.OriginalLine = originalLine;
                }
            }
        }
        return candidate;
    }

    private static void Generate(SourceProductionContext context, Compilation compilation,
        ImmutableArray<Candidate?> calls, string project, string projectName, string? artifacts)
    {
        var engine = new WriterEmitter(context, compilation);
        foreach (var candidate in calls)
        {
            if (candidate?.Root != null)
            {
                engine.Visit(candidate.Root, candidate.Syntax.GetLocation());
            }
        }

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            var type = attribute.AttributeClass;
            if (type?.OriginalDefinition.ToDisplayString() == "TheLithium.Imprint.SnapshotIncludeAttribute<T>"
                && type.TypeArguments.Length == 1)
            {
                engine.Visit(type.TypeArguments[0], attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None);
            }
        }
        var body = new StringBuilder();
        body.AppendLine("""
            // <auto-generated/>
            #nullable enable
            #pragma warning disable CS0618, CA2255, CS8600, CS8602, CS8603
            namespace TheLithium.Imprint.Generated
            {
                internal static class __SnapshotRegistration
                {
                    [global::System.Runtime.CompilerServices.ModuleInitializer]
                    internal static void Initialize()
                    {
            """);
        body.Append(engine.Registrations);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in calls)
        {
            if (candidate == null || !candidate.IsBoundary || candidate.Declaration == null || candidate.Caller == null)
            {
                continue;
            }

            var declaration = candidate.Declaration;
            var method = candidate.Caller;
            var location = declaration.GetLocation().GetMappedLineSpan();
            var file = candidate.OriginalFile ?? (string.IsNullOrEmpty(location.Path) ? declaration.SyntaxTree.FilePath : location.Path);
            var firstLine = candidate.OriginalLine ?? location.StartLinePosition.Line + 1;
            var lastLine = candidate.OriginalLine ?? location.EndLinePosition.Line + 1;
            var key = FormattableString.Invariant($"{file}:{firstLine}:{method.ToDisplayString()}");
            if (!seen.Add(key))
            {
                continue;
            }

            var suite = SuiteName(method.ContainingType);
            var configuredName = AttributeName(method.GetAttributes());
            var test = configuredName ?? method.Name;
            var displayName = configuredName is null ? DisplayName(method) : null;
            var update = AttributeUpdate(method.GetAttributes());
            if (update == 0)
            {
                update = ClassUpdate(method.ContainingType);
            }

            var requiresCase = method.Parameters.Any(parameter => !CompilerTypeFacts.IsCancellationToken(parameter.Type))
                || method.TypeParameters.Length != 0 || method.ContainingType.IsGenericType;
            var methodIdentity = method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                .WithMemberOptions(SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters)
                .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType));
            body.AppendLine(FormattableString.Invariant($$"""
                            global::TheLithium.Imprint.Generation.SnapshotMetadata.Register({{Literal(file)}}, {{firstLine}}, {{lastLine}},
                                new global::TheLithium.Imprint.Generation.Models.SnapshotDescriptor(
                                    {{Literal(project)}}, {{Literal(projectName)}}, {{Literal(file)}},
                                    {{Literal(suite)}}, {{Literal(test)}}, {{Literal(methodIdentity)}},
                                    (global::TheLithium.Imprint.SnapshotUpdate){{update}}, {{(requiresCase ? "true" : "false")}},
                                    {{(artifacts == null ? "null" : Literal(artifacts))}}, {{(displayName == null ? "null" : Literal(displayName))}}));
                """));
        }
        body.AppendLine("""
                    }
                }
            }
            """);
        context.AddSource("TheLithium.Imprint.Generated.g.cs", SourceText.From(body.ToString(), Encoding.UTF8));
    }

    private static int ClassUpdate(INamedTypeSymbol? type)
    {
        for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
        {
            for (INamedTypeSymbol? inherited = current; inherited != null; inherited = inherited.BaseType)
            {
                var update = AttributeUpdate(inherited.GetAttributes());
                if (update != 0)
                {
                    return update;
                }
            }
        }

        return 0;
    }

    private static string SuiteName(INamedTypeSymbol type)
    {
        var explicitName = AttributeName(type.GetAttributes());
        if (explicitName != null)
        {
            return explicitName;
        }

        var names = new Stack<string>();
        for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
        {
            names.Push(current.Name);
        }

        return string.Join(".", names);
    }

    private static int AttributeUpdate(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() == "TheLithium.Imprint.SnapshotSettingsAttribute")
            {
                foreach (var argument in attribute.NamedArguments)
                {
                    if (argument.Key == "Update" && argument.Value.Value is int value)
                    {
                        return value;
                    }
                }
            }
        }

        return 0;
    }

    private static string? AttributeName(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() == "TheLithium.Imprint.SnapshotSettingsAttribute")
            {
                foreach (var argument in attribute.NamedArguments)
                {
                    if (argument.Key == "Name" && argument.Value.Value is string value)
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }

    private static string? DisplayName(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            var type = attribute.AttributeClass;
            // Row metadata describes one invocation, not the entire parameterized method.
            if (IsRowAttribute(type))
            {
                continue;
            }

            var framework = false;
            for (var current = type; current != null; current = current.BaseType)
            {
                var ns = current.ContainingNamespace.ToDisplayString();
                if (ns == "Xunit" || ns == "NUnit.Framework" || ns == "Microsoft.VisualStudio.TestTools.UnitTesting")
                {
                    framework = true;
                    break;
                }
            }
            var descriptionAttribute = type?.ToDisplayString() == "System.ComponentModel.DescriptionAttribute";
            var namedDisplayMetadata = attribute.NamedArguments.Any(argument =>
                (argument.Key == "DisplayName" || argument.Key == "Description")
                && argument.Value.Value is string value && !string.IsNullOrWhiteSpace(value));
            var constructorDisplayMetadata = false;
            if (attribute.AttributeConstructor is { } constructor)
            {
                for (var index = 0; index < constructor.Parameters.Length; index++)
                {
                    if (constructor.Parameters[index].Name is "displayName" or "description"
                        && index < attribute.ConstructorArguments.Length
                        && attribute.ConstructorArguments[index].Value is string value
                        && !string.IsNullOrWhiteSpace(value))
                    {
                        constructorDisplayMetadata = true;
                        break;
                    }
                }
            }

            if (!framework && !descriptionAttribute && !namedDisplayMetadata && !constructorDisplayMetadata)
            {
                continue;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if ((argument.Key == "DisplayName" || argument.Key == "Description")
                    && argument.Value.Value is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            if (attribute.AttributeConstructor != null)
            {
                for (var index = 0; index < attribute.AttributeConstructor.Parameters.Length; index++)
                {
                    if (attribute.AttributeConstructor.Parameters[index].Name is "displayName" or "description"
                        && index < attribute.ConstructorArguments.Length
                        && attribute.ConstructorArguments[index].Value is string displayName
                        && !string.IsNullOrWhiteSpace(displayName))
                    {
                        return displayName;
                    }
                }
            }

            if (descriptionAttribute && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string description)
            {
                return description;
            }
        }
        return null;
    }

    private static bool IsRowAttribute(INamedTypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() is "Xunit.InlineDataAttribute"
                or "NUnit.Framework.TestCaseAttribute"
                or "Microsoft.VisualStudio.TestTools.UnitTesting.DataRowAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
    private static string TypeName(ITypeSymbol type) => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

}
