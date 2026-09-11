using Microsoft.CodeAnalysis;

namespace TheLithium.Imprint.Build;

internal static class CompilerTypeFacts
{
    internal static bool IsCancellationToken(ITypeSymbol type)
    {
        if (type.ToDisplayString() == "System.Threading.CancellationToken")
        {
            return true;
        }

        return type is INamedTypeSymbol nullable
            && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && nullable.TypeArguments.Length == 1
            && nullable.TypeArguments[0].ToDisplayString() == "System.Threading.CancellationToken";
    }

    internal static bool IsSupportedAsyncReturn(ITypeSymbol type)
        => type is INamedTypeSymbol named
            && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            && named.Name is "Task" or "ValueTask";
}
