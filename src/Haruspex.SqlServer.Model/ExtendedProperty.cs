using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// An extended property as a source sets it (<c>sp_addextendedproperty</c>, or a package's <c>SqlExtendedProperty</c>), before
/// anything is made of it: which properties are descriptions is the config file's to say, so the model keeps every one read and
/// the descriptions are found in them later (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
/// <param name="Name">The property's name, such as <c>MS_Description</c>; null when the source doesn't give it as a literal.</param>
/// <param name="Value">Its value as text; null when it is <c>NULL</c>, or isn't text a source gives as a literal (<see cref="Unread"/> says which).</param>
/// <param name="Levels">What it is set on, from the outermost level: <c>SCHEMA</c>, then <c>TABLE</c>, then <c>COLUMN</c>, each a type and a name.</param>
/// <param name="At">Where it is set.</param>
public sealed record ExtendedProperty(string? Name, string? Value, EquatableArray<PropertyLevel> Levels, SourceLocation At)
{
    /// <summary>What the statement does with the property: adds it, by default, or updates or drops it.</summary>
    public PropertyAction Action { get; init; } = PropertyAction.Add;

    /// <summary>
    /// Why the property can't be read, a sentence naming the argument (its value is a variable, or a number); null when every
    /// argument was read.
    /// </summary>
    public string? Unread { get; init; }
}
