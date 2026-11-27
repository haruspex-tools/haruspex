using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

internal sealed partial class Typer
{
    /// <summary>
    /// What a parameter declared to hold JSON of a schema holds where its body starts: <paramref name="declared"/>, what every caller is
    /// claimed to send, joined with its default where it has one other than NULL, since a caller that leaves it out sends the default,
    /// which no declaration covers (docs/decisions/0143-a-parameter-s-json-may-be-declared.md). A default that is literal text is the
    /// JSON it parses to, or text that isn't JSON (whose reads then say why); any other default is unknown, which the join keeps.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="declared">Its declared value: JSON text of the schema's type, or live JSON for a <c>json</c> parameter, and its null reasons.</param>
    internal static Value WithDefault(Parameter parameter, Value declared)
    {
        if (parameter.Default is not { } fallback || Strip(fallback) is NullLiteral)
            return declared;

        Value sent;
        if (LiteralOf(fallback) is ({ } literal and StringLiteral { Collation: null } text, false))
        {
            var parsed = JsonLiterals.Parse(literal.Value, text.IsNational, parameter.At);
            sent = declared.IsLiveJson
                ? new Value(parsed, [], IsLiveJson: true)
                : Value.Computed(new JsonTextShape(parsed) { Text = (declared.Shape as JsonTextShape)?.Text }, []);
        }
        else
        {
            sent = Value.Unknown(new UnknownShape(IrUnknownReason.UnsupportedExpression, $"{parameter.Name} is declared to hold JSON, and its default, which a caller that leaves it out sends, is not literal text.", parameter.At));
        }

        return Value.Join(declared, sent, parameter.At, parameter.Name);
    }
}
