using System.Linq;
using Microsoft.CodeAnalysis;

namespace TheLithium.Imprint.Build;

internal static class CompilerTypeFacts
{
    private const string CaptureMethodAttribute = "TheLithium.Imprint.Generation.SnapshotCaptureMethodAttribute";
    private const string CaptureTypeAttribute = "TheLithium.Imprint.Generation.SnapshotCaptureTypeAttribute";

    /// <summary>The built-in assertions, or any method marked as a capture, such as AssertImage.</summary>
    internal static bool IsCapture(IMethodSymbol method)
        => method.ContainingType.ToDisplayString() == "TheLithium.Imprint.SnapshotExtensions"
            && (method.Name == "AssertSnapshot" || method.Name == "UpdateSnapshot")
            || method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == CaptureMethodAttribute);

    /// <summary>The method a type names for its own captures, such as AssertImage, or null for an ordinary type.</summary>
    internal static string? DedicatedCapture(ITypeSymbol type)
    {
        var marker = type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == CaptureTypeAttribute);
        if (marker is null || marker.ConstructorArguments.Length != 1)
        {
            return null;
        }

        return marker.ConstructorArguments[0].Value as string;
    }

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
