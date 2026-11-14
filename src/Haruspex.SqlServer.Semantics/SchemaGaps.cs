using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// Where a name that binds to nothing may be defined: a part of the database the source refers to but which wasn't read
/// (<see cref="DatabaseModel.Unread"/>), such as a referenced DACPAC that wasn't found. That can't be proven to be the cause,
/// so a message says "may"; it sends the reader to the one problem rather than to chase each name.
/// </summary>
public static class SchemaGaps
{
    /// <summary>A sentence to add to a message that says a name is missing: empty when nothing was left unread.</summary>
    /// <param name="model">The schema the name was looked for in.</param>
    public static string MayBeIn(DatabaseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Unread.Count == 0
            ? ""
            : $" It may be defined in {string.Join("; or in ", model.Unread.Select(p => $"{p.Name}, which {p.ReferredToBy} refers to and which was not found"))}.";
    }
}
