using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Reads a script's <c>EXEC sp_addextendedproperty</c>, <c>sp_updateextendedproperty</c> and <c>sp_dropextendedproperty</c>,
/// with arguments by position, by name, or by position and then by name, as the server takes them (observed). Nothing is
/// guessed: an argument that isn't a literal leaves the property unread, saying which
/// (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
internal static class ExtendedPropertyCalls
{
    /// <summary>The parameters of <c>sp_addextendedproperty</c> and <c>sp_updateextendedproperty</c>, in order.</summary>
    private static readonly string[] Setting = ["@name", "@value", "@level0type", "@level0name", "@level1type", "@level1name", "@level2type", "@level2name"];

    /// <summary>The parameters of <c>sp_dropextendedproperty</c>, which takes no value, in order.</summary>
    private static readonly string[] Dropping = ["@name", "@level0type", "@level0name", "@level1type", "@level1name", "@level2type", "@level2name"];

    /// <summary>
    /// The property <paramref name="statement"/> sets, updates or drops; null when it calls no such procedure. A procedure of
    /// that name in another schema than <c>sys</c> (or none) is the database's own, and isn't one.
    /// </summary>
    /// <param name="statement">The statement.</param>
    /// <param name="file">The script's path, which its location names.</param>
    public static ExtendedProperty? Read(ExecuteStatement statement, string file)
    {
        ArgumentNullException.ThrowIfNull(statement);
        if (statement.ExecuteSpecification?.ExecutableEntity is not ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name: { } name } call
            || (name.DatabaseIdentifier is not null && !name.DatabaseIdentifier.Value.Equals("master", StringComparison.OrdinalIgnoreCase))
            || (name.SchemaIdentifier is not null && !name.SchemaIdentifier.Value.Equals("sys", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var action = name.BaseIdentifier.Value.ToUpperInvariant() switch
        {
            "SP_ADDEXTENDEDPROPERTY" => PropertyAction.Add,
            "SP_UPDATEEXTENDEDPROPERTY" => PropertyAction.Update,
            "SP_DROPEXTENDEDPROPERTY" => PropertyAction.Drop,
            _ => (PropertyAction?)null,
        };
        if (action is not { } act)
            return null;

        var at = SyntaxReader.Span(file, statement).Start;
        var names = act == PropertyAction.Drop ? Dropping : Setting;
        var given = new Dictionary<string, ScalarExpression>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < call.Parameters.Count; i++)
        {
            var parameter = call.Parameters[i];
            var key = parameter.Variable?.Name ?? (i < names.Length ? names[i] : null);
            if (key is null || !names.Contains(key, StringComparer.OrdinalIgnoreCase) || !given.TryAdd(key, parameter.ParameterValue))
                return new ExtendedProperty(null, null, [], at) { Action = act, Unread = $"Its argument {i + 1} isn't one the procedure takes." };
        }

        string? unread = null;
        string? Argument(string parameter)
        {
            if (!given.TryGetValue(parameter, out var value))
                return null;
            switch (Literal(value))
            {
                case (true, var text):
                    return text;
                default:
                    // A value the server keeps as another type than text, such as 42, an int (observed), is no description.
                    unread ??= value is Literal && parameter == "@value"
                        ? "Its value isn't text."
                        : $"Its argument {parameter} isn't a string literal, an identifier or NULL.";
                    return null;
            }
        }

        var property = Argument("@name");
        var text = act == PropertyAction.Drop ? null : Argument("@value");
        var levels = new List<PropertyLevel>();
        for (var level = 0; level < 3; level++)
        {
            var type = Argument($"@level{level}type");
            var levelName = Argument($"@level{level}name");
            if (type is null)
                break;
            if (levelName is null)
            {
                unread ??= $"Its argument @level{level}name is NULL, so what it is set on isn't one object.";
                break;
            }

            levels.Add(new PropertyLevel(type, levelName));
        }

        return new ExtendedProperty(property, text, [.. levels], at) { Action = act, Unread = unread };
    }

    /// <summary>
    /// An argument's text when it is a literal the server takes as a string: a string literal, or an identifier (<c>[Person]</c>,
    /// the string <c>Person</c>: observed); null text for <c>NULL</c>; not read for anything else.
    /// </summary>
    private static (bool Read, string? Text) Literal(ScalarExpression value) => value switch
    {
        StringLiteral text => (true, text.Value),
        IdentifierLiteral identifier => (true, identifier.Value),
        NullLiteral => (true, null),
        _ => (false, null),
    };
}
