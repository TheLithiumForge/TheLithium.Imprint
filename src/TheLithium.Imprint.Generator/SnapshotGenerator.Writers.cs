using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace TheLithium.Imprint.Generator;

public sealed partial class SnapshotGenerator
{
    private sealed class WriterEmitter
    {
        private readonly SourceProductionContext _context;
        private readonly Compilation _compilation;
        private readonly HashSet<ITypeSymbol> _visited = new(SymbolEqualityComparer.Default);
        internal readonly StringBuilder Registrations = new();

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
                EmitArray(code, array.Rank, level: 0, indices: []);
            }
            else if (type is INamedTypeSymbol named)
            {
                if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    dependencies.Add(named.TypeArguments[0]);
                    code.AppendLine("""
                        if (!value.HasValue) writer.WriteNullValue();
                        else global::TheLithium.Imprint.SnapshotWriters.Write(writer, value.Value, context);
                        """);
                }
                else if (named.TypeKind == TypeKind.Enum)
                {
                    EmitEnum(code, named, location);
                }
                else if (named.IsTupleType)
                {
                    code.AppendLine("writer.WriteStartObject();");
                    for (var i = 0; i < named.TupleElements.Length; i++)
                    {
                        dependencies.Add(named.TupleElements[i].Type);
                        var memberName = FormattableString.Invariant($"Item{i + 1}");
                        EmitMember(code, memberName, $"value.{memberName}");
                    }
                    code.AppendLine("writer.WriteEndObject();");
                }
                else
                {
                    var dictionary = Interface(named, "System.Collections.Generic.IDictionary<TKey, TValue>")
                        ?? Interface(named, "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>");
                    var enumerable = Interface(named, "System.Collections.Generic.IEnumerable<T>");
                    if (dictionary != null && dictionary.TypeArguments[0].SpecialType == SpecialType.System_String)
                    {
                        dependencies.Add(dictionary.TypeArguments[1]);
                        var entryType = _compilation.GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2");
                        if (entryType is null)
                        {
                            Fail(type, location, "the dictionary entry type could not be resolved");
                            return;
                        }
                        dependencies.Add(entryType.Construct(dictionary.TypeArguments.ToArray()));
                        code.AppendLine("if (context.Representation.Dictionaries == global::TheLithium.Imprint.SnapshotDictionaryRepresentation.Entries)");
                        code.AppendLine("""
                            {
                                writer.WriteStartArray();
                                int entryIndex = 0;
                                foreach (var entry in value)
                                    using (context.At(entryIndex++)) global::TheLithium.Imprint.SnapshotWriters.Write(writer, entry, context);
                                writer.WriteEndArray();
                                return;
                            }
                            """);
                        code.AppendLine("""
                            writer.WriteStartObject();
                            foreach (var item in value)
                            {
                                if (item.Key is null) throw new global::TheLithium.Imprint.SnapshotCaptureException("A JSON dictionary key cannot be null.");
                                writer.WritePropertyName(item.Key);
                                using (context.At(item.Key)) global::TheLithium.Imprint.SnapshotWriters.Write(writer, item.Value, context);
                            }
                            writer.WriteEndObject();
                            """);
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
                        code.AppendLine("""
                            writer.WriteStartArray();
                            int index = 0;
                            foreach (var item in value)
                                using (context.At(index++)) global::TheLithium.Imprint.SnapshotWriters.Write(writer, item, context);
                            writer.WriteEndArray();
                            """);
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
                        code.AppendLine("writer.WriteStartObject();");
                        foreach (var member in members)
                        {
                            var memberType = member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type;
                            dependencies.Add(memberType);
                            var access = ContainsAnonymous(named) ? $"value.@{member.Name}" : MemberAccess(named, member);
                            EmitMember(code, member.Name, access);
                        }
                        code.AppendLine("writer.WriteEndObject();");
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

            Registrations.AppendLine($"            global::TheLithium.Imprint.SnapshotWriters.TryRegister({shape}, static (writer, value, context) =>");
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
            return $"(({TypeName(declaringType)})value).@{member.Name}";
        }

        private static void EmitMember(StringBuilder code, string name, string access)
        {
            code.AppendLine($$"""
                writer.WritePropertyName({{Literal(name)}});
                using (context.At({{Literal(name)}}))
                {
                    try { global::TheLithium.Imprint.SnapshotWriters.Write(writer, {{access}}, context); }
                    catch (global::TheLithium.Imprint.SnapshotException) { throw; }
                    catch (global::System.OperationCanceledException) { throw; }
                    catch (global::System.Exception error) { throw new global::TheLithium.Imprint.SnapshotCaptureException($"Could not read the member at {context.Path}.", error); }
                }
                """);
        }

        private static void EmitArray(StringBuilder code, int rank, int level, List<string> indices)
        {
            var index = FormattableString.Invariant($"index{level}");
            code.AppendLine(FormattableString.Invariant($$"""
                if (value.GetLowerBound({{level}}) != 0)
                    throw new global::TheLithium.Imprint.SnapshotCaptureException("Non-zero-based arrays need an explicit writer.");
                writer.WriteStartArray();
                for (int {{index}} = 0; {{index}} < value.GetLength({{level}}); {{index}}++)
                {
                    using (context.At({{index}}))
                    {
                """));
            indices.Add(index);
            if (level + 1 < rank)
            {
                EmitArray(code, rank, level + 1, indices);
            }
            else
            {
                code.AppendLine($"global::TheLithium.Imprint.SnapshotWriters.Write(writer, value[{string.Join(", ", indices)}], context);");
            }

            indices.RemoveAt(indices.Count - 1);
            code.AppendLine("""
                    }
                }
                writer.WriteEndArray();
                """);
        }

        private void EmitEnum(StringBuilder code, INamedTypeSymbol type, Location location)
        {
            if (type.EnumUnderlyingType is not { } underlyingType)
            {
                Fail(type, location, "the enum underlying type could not be resolved");
                return;
            }
            code.AppendLine($$"""
                if (context.Representation.Enums == global::TheLithium.Imprint.SnapshotEnumRepresentation.Number)
                {
                    writer.WriteNumberValue(({{TypeName(underlyingType)}})value);
                    return;
                }
                """);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            code.AppendLine("switch (value)");
            code.AppendLine("{");
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(x => x.HasConstantValue))
            {
                var key = Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture) ?? "";
                if (!seen.Add(key))
                {
                    continue;
                }

                code.AppendLine($"case {TypeName(type)}.@{field.Name}: writer.WriteStringValue({Literal(field.Name)}); break;");
            }
            code.AppendLine($"default: writer.WriteNumberValue(({TypeName(underlyingType)})value); break;");
            code.AppendLine("}");
        }

        private string? Shape(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String)
            {
                // Shape samples are never read. A non-null sample preserves notnull
                // key constraints when an anonymous dictionary is inferred.
                return "global::System.String.Empty";
            }
            if (!ContainsAnonymous(type))
            {
                if (!_compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly))
                {
                    return null;
                }

                return $"default({TypeName(type)})";
            }
            if (type is IArrayTypeSymbol array)
            {
                var child = Shape(array.ElementType);
                if (child == null || array.Rank > 3)
                {
                    return null;
                }

                var rankSuffix = array.Rank == 1 ? "" : array.Rank.ToString(CultureInfo.InvariantCulture);
                return $"global::TheLithium.Imprint.Generation.Shapes.Array{rankSuffix}({child})";
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

                    fields.Add($"@{property.Name} = {child}");
                }
                return $"new {{ {string.Join(", ", fields)} }}";
            }
            if (named.IsTupleType)
            {
                var elements = named.TupleElements.Select(x => Shape(x.Type)).ToArray();
                if (elements.Any(x => x == null))
                {
                    return null;
                }

                return $"({string.Join(", ", elements)})";
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

            return $"global::TheLithium.Imprint.Generation.Shapes.{helper}({string.Join(", ", arguments)})";
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
            if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
            {
                return true;
            }
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
