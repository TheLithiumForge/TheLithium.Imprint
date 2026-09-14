using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TheLithium.Imprint.Build;

/// <summary>Finds callers as well as capture helpers so approval belongs to the outer test method.</summary>
internal static class SnapshotCallGraph
{
    internal static HashSet<IMethodSymbol> FindLifetimes(CSharpCompilation compilation)
    {
        var reachable = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var calls = new Dictionary<IMethodSymbol, IMethodSymbol[]>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (method.Body == null && method.ExpressionBody == null)
                {
                    continue;
                }
                if (!(model.GetDeclaredSymbol(method) is IMethodSymbol symbol))
                {
                    continue;
                }

                var invoked = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Select(invocation => model.GetSymbolInfo(invocation).Symbol)
                    .OfType<IMethodSymbol>().Select(called => (called.ReducedFrom ?? called).OriginalDefinition).ToArray();
                if (invoked.Any(IsExplicitLifetime))
                {
                    continue;
                }

                calls.Add(symbol.OriginalDefinition, invoked);
                if (invoked.Any(IsCapture) || symbol.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "TheLithium.Imprint.SnapshotSettingsAttribute"))
                {
                    reachable.Add(symbol.OriginalDefinition);
                }
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var entry in calls)
            {
                if (entry.Value.Any(reachable.Contains))
                {
                    changed |= reachable.Add(entry.Key);
                }
            }
        } while (changed);

        return reachable;
    }

    private static bool IsCapture(IMethodSymbol method)
        => method.ContainingType.ToDisplayString() == "TheLithium.Imprint.SnapshotExtensions"
            && (method.Name == "AssertSnapshot" || method.Name == "UpdateSnapshot");

    private static bool IsExplicitLifetime(IMethodSymbol method)
        => method.ContainingType.ToDisplayString() == "TheLithium.Imprint.Snapshots"
            && (method.Name == "Run" || method.Name == "RunAsync" || method.Name == "Begin");
}
