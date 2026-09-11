using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace TheLithium.Imprint.Build;

/// <summary>Preserves compiler-supplied caller member names when a body becomes a local function.</summary>
internal static class CallerInformation
{
    internal static string Preserve(SyntaxNode body, SemanticModel model)
    {
        var text = SourceText.From(body.ToString());
        var changes = new List<TextChange>();
        foreach (var call in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (!(model.GetOperation(call) is IInvocationOperation operation))
            {
                continue;
            }
            var arguments = new List<string>();
            foreach (var argument in operation.Arguments)
            {
                var parameter = argument.Parameter;
                if (argument.IsImplicit && parameter != null && argument.Value.ConstantValue.Value is string member
                    && parameter.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
                        == "System.Runtime.CompilerServices.CallerMemberNameAttribute"))
                {
                    arguments.Add($"@{parameter.Name}: {SymbolDisplay.FormatLiteral(member, quote: true)}");
                }
            }
            if (arguments.Count != 0)
            {
                var separator = call.ArgumentList.Arguments.Count == 0 ? string.Empty : ", ";
                changes.Add(new TextChange(new TextSpan(call.ArgumentList.CloseParenToken.SpanStart - body.SpanStart, 0),
                    separator + string.Join(", ", arguments)));
            }
        }
        return text.WithChanges(changes).ToString();
    }
}
