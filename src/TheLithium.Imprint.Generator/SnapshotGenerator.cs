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

namespace TheLithium.Imprint.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class SnapshotGenerator : IIncrementalGenerator
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
        return name == "Snapshot" || name == "AssertSnapshot" || name == "UpdateSnapshot" || name == "Create"
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
        var capture = owner == "TheLithium.Imprint.SnapshotExtensions" && (method.Name == "Snapshot" || method.Name == "AssertSnapshot" || method.Name == "UpdateSnapshot");
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
        body.AppendLine("// <auto-generated/>");
        body.AppendLine("#nullable enable");
        body.AppendLine("#pragma warning disable CS0618, CA2255, CS8600, CS8602, CS8603");
        body.AppendLine("namespace TheLithium.Imprint.Generated");
        body.AppendLine("{");
        body.AppendLine("    internal static class __SnapshotRegistration");
        body.AppendLine("    {");
        body.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        body.AppendLine("        internal static void Initialize()");
        body.AppendLine("        {");
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
            var key = file + ":" + firstLine.ToString(CultureInfo.InvariantCulture)
                + ":" + method.ToDisplayString();
            if (!seen.Add(key))
            {
                continue;
            }

            var suite = SuiteName(method.ContainingType);
            var test = AttributeName(method.GetAttributes()) ?? method.Name;
            var displayName = AttributeName(method.GetAttributes()) is null ? DisplayName(method) : null;
            var update = AttributeUpdate(method.GetAttributes());
            if (update == 0)
            {
                update = ClassUpdate(method.ContainingType);
            }

            var requiresCase = method.Parameters.Any(parameter => parameter.Type.ToDisplayString() != "System.Threading.CancellationToken")
                || method.TypeParameters.Length != 0 || method.ContainingType.IsGenericType;
            body.Append("            global::TheLithium.Imprint.Generation.SnapshotMetadata.Register(")
                .Append(Literal(file)).Append(", ")
                .Append(firstLine).Append(", ")
                .Append(lastLine).AppendLine(",");
            body.Append("                new global::TheLithium.Imprint.Generation.Models.SnapshotDescriptor(")
                .Append(Literal(project)).Append(", ").Append(Literal(projectName)).Append(", ")
                .Append(Literal(file)).Append(", ").Append(Literal(suite)).Append(", ").Append(Literal(test)).Append(", ")
                .Append(Literal(method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithMemberOptions(SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters)
                    .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType)))).Append(", ")
                .Append("(global::TheLithium.Imprint.SnapshotUpdate)").Append(update).Append(", ")
                .Append(requiresCase ? "true" : "false").Append(", ")
                .Append(artifacts == null ? "null" : Literal(artifacts)).Append(", ")
                .Append(displayName == null ? "null" : Literal(displayName)).AppendLine("));");
        }
        body.AppendLine("        }");
        body.AppendLine("    }");
        body.AppendLine("}");
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
            if (type?.Name == "TestCaseAttribute" || type?.Name == "DataRowAttribute" || type?.Name == "InlineDataAttribute")
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

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
    private static string TypeName(ITypeSymbol type) => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private sealed class WriterEmitter
    {
        private readonly SourceProductionContext _context;
        private readonly Compilation _compilation;
        private readonly HashSet<ITypeSymbol> _visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        internal readonly StringBuilder Registrations = new StringBuilder();

        internal WriterEmitter(SourceProductionContext context, Compilation compilation)
        {
            _context = context;
            _compilation = compilation;
        }

        internal void Visit(ITypeSymbol type, Location location)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            if (!_visited.Add(type) || Builtin(type))
            {
                return;
            }
            // Open generic helper sites are rooted through SnapshotInclude<T> or explicit writers.
            if (ContainsTypeParameter(type))
            {
                return;
            }

            if (type.TypeKind == TypeKind.Error)
            {
                return;
            }

            if (type.TypeKind == TypeKind.Dynamic || type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer
                || (type is INamedTypeSymbol refType && refType.IsRefLikeType))
            {
                Fail(type, location, "dynamic, pointer and ref-like values are not supported");
                return;
            }
            var shape = Shape(type);
            if (shape == null)
            {
                Fail(type, location, "the type cannot be named or its anonymous generic shape is unsupported");
                return;
            }
            var code = new StringBuilder();
            var dependencies = new List<ITypeSymbol>();
            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank > 3)
                {
                    Fail(type, location, "only one-, two- and three-dimensional arrays are generated");
                    return;
                }
                dependencies.Add(array.ElementType);
                EmitArray(code, array.Rank, 0, new List<string>());
            }
            else if (type is INamedTypeSymbol named)
            {
                if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    dependencies.Add(named.TypeArguments[0]);
                    code.AppendLine("if (!v.HasValue) w.WriteNullValue();");
                    code.AppendLine("else global::TheLithium.Imprint.SnapshotWriters.Write(w, v.Value, c);");
                }
                else if (named.TypeKind == TypeKind.Enum)
                {
                    EmitEnum(code, named, location);
                }
                else if (named.IsTupleType)
                {
                    code.AppendLine("w.WriteStartObject();");
                    for (var i = 0; i < named.TupleElements.Length; i++)
                    {
                        dependencies.Add(named.TupleElements[i].Type);
                        EmitMember(code, "Item" + (i + 1).ToString(CultureInfo.InvariantCulture),
                            "v.Item" + (i + 1).ToString(CultureInfo.InvariantCulture));
                    }
                    code.AppendLine("w.WriteEndObject();");
                }
                else
                {
                    var dictionary = Interface(named, "System.Collections.Generic.IDictionary<TKey, TValue>")
                        ?? Interface(named, "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>");
                    var enumerable = Interface(named, "System.Collections.Generic.IEnumerable<T>");
                    if (dictionary != null && dictionary.TypeArguments[0].SpecialType == SpecialType.System_String)
                    {
                        dependencies.Add(dictionary.TypeArguments[1]);
                        code.AppendLine("w.WriteStartObject();");
                        code.AppendLine("foreach (var item in v)");
                        code.AppendLine("{");
                        code.AppendLine("    if (item.Key is null) throw new global::TheLithium.Imprint.SnapshotCaptureException(\"A JSON dictionary key cannot be null.\");");
                        code.AppendLine("    w.WritePropertyName(item.Key);");
                        code.AppendLine("    using (c.At(item.Key)) global::TheLithium.Imprint.SnapshotWriters.Write(w, item.Value, c);");
                        code.AppendLine("}");
                        code.AppendLine("w.WriteEndObject();");
                    }
                    else if (enumerable != null)
                    {
                        var element = enumerable.TypeArguments[0];
                        var alternatives = named.AllInterfaces.Where(x => x.OriginalDefinition.ToDisplayString()
                            == "System.Collections.Generic.IEnumerable<T>").Select(x => x.TypeArguments[0]).Distinct(SymbolEqualityComparer.Default).Count();
                        if (alternatives > 1)
                        {
                            Fail(type, location, "multiple IEnumerable<T> element types are ambiguous");
                            return;
                        }
                        dependencies.Add(element);
                        code.AppendLine("w.WriteStartArray();");
                        code.AppendLine("int index = 0;");
                        code.AppendLine("foreach (var item in v)");
                        code.AppendLine("    using (c.At(index++)) global::TheLithium.Imprint.SnapshotWriters.Write(w, item, c);");
                        code.AppendLine("w.WriteEndArray();");
                    }
                    else
                    {
                        var ns = named.ContainingNamespace.ToDisplayString();
                        var pair = named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.KeyValuePair<TKey, TValue>";
                        if (named.SpecialType == SpecialType.System_Object
                            || (!named.IsAnonymousType && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)) && !pair)
                            || named.TypeKind == TypeKind.Delegate)
                        {
                            Fail(type, location, "opaque runtime/framework types need an explicit representation");
                            return;
                        }
                        var members = PublicMembers(named);
                        if (members.Count == 0)
                        {
                            Fail(type, location, "no accessible public instance fields or readable properties were found");
                            return;
                        }
                        code.AppendLine("w.WriteStartObject();");
                        foreach (var member in members)
                        {
                            var memberType = member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type;
                            dependencies.Add(memberType);
                            var access = named.IsAnonymousType ? "v.@" + member.Name : MemberAccess(named, member);
                            EmitMember(code, member.Name, access);
                        }
                        code.AppendLine("w.WriteEndObject();");
                    }
                }
            }
            else
            {
                Fail(type, location, "the static shape is unsupported");
                return;
            }

            foreach (var dependency in dependencies)
            {
                Visit(dependency, location);
            }

            Registrations.Append("            global::TheLithium.Imprint.SnapshotWriters.TryRegister(").Append(shape).AppendLine(", static (w, v, c) =>");
            Registrations.AppendLine("            {");
            foreach (var line in code.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Registrations.Append("                ").AppendLine(line.TrimEnd('\r'));
            }

            Registrations.AppendLine("            });");
        }

        private List<ISymbol> PublicMembers(INamedTypeSymbol type)
        {
            var members = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
            var types = new List<INamedTypeSymbol>();
            for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
            {
                types.Add(current);
            }

            if (type.TypeKind == TypeKind.Interface)
            {
                types.AddRange(type.AllInterfaces);
            }

            foreach (var declaring in types)
            {
                foreach (var member in declaring.GetMembers())
                {
                    if (member.IsStatic || members.ContainsKey(member.Name))
                    {
                        continue;
                    }

                    if (member is IPropertySymbol property && !property.IsIndexer
                        && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
                        && property.ExplicitInterfaceImplementations.Length == 0)
                    {
                        members.Add(property.Name, property);
                    }
                    else if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public
                        && !field.IsConst && !field.IsImplicitlyDeclared && !field.IsFixedSizeBuffer)
                    {
                        members.Add(field.Name, field);
                    }
                }
            }

            return members.Values.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        }

        private static string MemberAccess(INamedTypeSymbol fallbackType, ISymbol member)
        {
            var declaringType = member.ContainingType ?? fallbackType;
            return "((" + TypeName(declaringType) + ")v).@" + member.Name;
        }

        private static void EmitMember(StringBuilder code, string name, string access)
        {
            code.Append("w.WritePropertyName(").Append(Literal(name)).AppendLine(");");
            code.Append("using (c.At(").Append(Literal(name)).AppendLine("))");
            code.AppendLine("{");
            code.Append("    try { global::TheLithium.Imprint.SnapshotWriters.Write(w, ").Append(access).AppendLine(", c); }");
            code.AppendLine("    catch (global::TheLithium.Imprint.SnapshotException) { throw; }");
            code.AppendLine("    catch (global::System.OperationCanceledException) { throw; }");
            code.AppendLine("    catch (global::System.Exception error) { throw new global::TheLithium.Imprint.SnapshotCaptureException(\"Could not read the member at \" + c.Path + \".\", error); }");
            code.AppendLine("}");
        }

        private static void EmitArray(StringBuilder code, int rank, int level, List<string> indices)
        {
            var index = "i" + level.ToString(CultureInfo.InvariantCulture);
            code.Append("if (v.GetLowerBound(").Append(level).AppendLine(") != 0)");
            code.AppendLine("    throw new global::TheLithium.Imprint.SnapshotCaptureException(\"Non-zero-based arrays need an explicit writer.\");");
            code.AppendLine("w.WriteStartArray();");
            code.Append("for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < v.GetLength(")
                .Append(level).Append("); ").Append(index).AppendLine("++)");
            code.AppendLine("{");
            code.Append("    using (c.At(").Append(index).AppendLine("))");
            code.AppendLine("    {");
            indices.Add(index);
            if (level + 1 < rank)
            {
                EmitArray(code, rank, level + 1, indices);
            }
            else
            {
                code.Append("global::TheLithium.Imprint.SnapshotWriters.Write(w, v[").Append(string.Join(", ", indices)).AppendLine("], c);");
            }

            indices.RemoveAt(indices.Count - 1);
            code.AppendLine("    }");
            code.AppendLine("}");
            code.AppendLine("w.WriteEndArray();");
        }

        private void EmitEnum(StringBuilder code, INamedTypeSymbol type, Location location)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            code.AppendLine("switch (v)");
            code.AppendLine("{");
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(x => x.HasConstantValue))
            {
                var key = Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture) ?? "";
                if (!seen.Add(key))
                {
                    continue;
                }

                code.Append("case ").Append(TypeName(type)).Append(".@").Append(field.Name)
                    .Append(": w.WriteStringValue(").Append(Literal(field.Name)).AppendLine("); break;");
            }
            if (type.EnumUnderlyingType is not { } underlyingType)
            {
                Fail(type, location, "the enum underlying type could not be resolved");
                return;
            }

            code.Append("default: w.WriteNumberValue((").Append(TypeName(underlyingType)).AppendLine(")v); break;");
            code.AppendLine("}");
        }

        private string? Shape(ITypeSymbol type)
        {
            if (!ContainsAnonymous(type))
            {
                if (!_compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly))
                {
                    return null;
                }

                return "default(" + TypeName(type) + ")";
            }
            if (type is IArrayTypeSymbol array)
            {
                var child = Shape(array.ElementType);
                if (child == null || array.Rank > 3)
                {
                    return null;
                }

                return "global::TheLithium.Imprint.Generation.Shapes.Array" + (array.Rank == 1 ? "" : array.Rank.ToString(CultureInfo.InvariantCulture)) + "(" + child + ")";
            }
            if (!(type is INamedTypeSymbol named))
            {
                return null;
            }

            if (named.IsAnonymousType)
            {
                var fields = new List<string>();
                // Preserve declaration order: anonymous type identity includes property order.
                foreach (var property in named.GetMembers().OfType<IPropertySymbol>())
                {
                    var child = Shape(property.Type);
                    if (child == null)
                    {
                        return null;
                    }

                    fields.Add("@" + property.Name + " = " + child);
                }
                return "new { " + string.Join(", ", fields) + " }";
            }
            if (named.IsTupleType)
            {
                var elements = named.TupleElements.Select(x => Shape(x.Type)).ToArray();
                if (elements.Any(x => x == null))
                {
                    return null;
                }

                return "(" + string.Join(", ", elements) + ")";
            }
            var definition = named.OriginalDefinition.ToDisplayString();
            string? helper = null;
            switch (definition)
            {
                case "System.Collections.Generic.List<T>":
                    helper = "List";
                    break;
                case "System.Collections.Generic.IEnumerable<T>":
                    helper = "Enumerable";
                    break;
                case "System.Collections.Generic.IList<T>":
                    helper = "IList";
                    break;
                case "System.Collections.Generic.ICollection<T>":
                    helper = "ICollection";
                    break;
                case "System.Collections.Generic.IReadOnlyList<T>":
                    helper = "ReadOnlyList";
                    break;
                case "System.Collections.Generic.IReadOnlyCollection<T>":
                    helper = "ReadOnlyCollection";
                    break;
                case "System.Collections.Generic.HashSet<T>":
                    helper = "HashSet";
                    break;
                case "System.Collections.Generic.ISet<T>":
                    helper = "ISet";
                    break;
                case "System.Collections.Generic.Dictionary<TKey, TValue>":
                    helper = "Dictionary";
                    break;
                case "System.Collections.Generic.IDictionary<TKey, TValue>":
                    helper = "IDictionary";
                    break;
                case "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                    helper = "ReadOnlyDictionary";
                    break;
                case "System.Collections.Generic.KeyValuePair<TKey, TValue>":
                    helper = "Pair";
                    break;
            }
            if (helper == null)
            {
                return null;
            }

            var arguments = named.TypeArguments.Select(Shape).ToArray();
            if (arguments.Any(x => x == null))
            {
                return null;
            }

            return "global::TheLithium.Imprint.Generation.Shapes." + helper + "(" + string.Join(", ", arguments) + ")";
        }

        private static bool ContainsAnonymous(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array)
            {
                return ContainsAnonymous(array.ElementType);
            }

            return type is INamedTypeSymbol named && (named.IsAnonymousType || named.TypeArguments.Any(ContainsAnonymous));
        }

        private static bool ContainsTypeParameter(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.TypeParameter)
            {
                return true;
            }

            if (type is IArrayTypeSymbol array)
            {
                return ContainsTypeParameter(array.ElementType);
            }

            return type is INamedTypeSymbol named && (named.IsUnboundGenericType || named.TypeArguments.Any(ContainsTypeParameter)
                || (named.ContainingType != null && ContainsTypeParameter(named.ContainingType)));
        }

        private static INamedTypeSymbol? Interface(INamedTypeSymbol type, string definition)
        {
            if (type.OriginalDefinition.ToDisplayString() == definition)
            {
                return type;
            }

            return type.AllInterfaces.FirstOrDefault(x => x.OriginalDefinition.ToDisplayString() == definition);
        }

        private static bool Builtin(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                case SpecialType.System_Char:
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Decimal:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    return true;
            }
            var name = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
            return name == "System.Half" || name == "System.Int128" || name == "System.UInt128"
                || name == "System.Numerics.BigInteger" || name == "System.Guid" || name == "System.DateTime"
                || name == "System.DateTimeOffset" || name == "System.DateOnly" || name == "System.TimeOnly"
                || name == "System.TimeSpan" || name == "System.Uri" || name == "System.Text.Json.JsonElement"
                || name == "System.Text.Json.JsonDocument";
        }

        private void Fail(ITypeSymbol type, Location location, string reason)
            => _context.ReportDiagnostic(Diagnostic.Create(Unsupported, location, type.ToDisplayString(), reason));
    }
}
