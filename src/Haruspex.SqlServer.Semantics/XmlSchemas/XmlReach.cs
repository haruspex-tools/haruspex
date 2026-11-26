namespace Haruspex.SqlServer.Semantics.XmlSchemas;

/// <summary>
/// What a path reaches: the declaration of its nodes and how many there are, at least one or at most one, or why nothing is proved.
/// </summary>
/// <param name="Node">The declaration of the nodes it reaches; null when <see cref="Why"/> says why it isn't followed.</param>
/// <param name="AtLeastOne">Whether it reaches a node in every instance it starts from.</param>
/// <param name="AtMostOne">Whether it reaches no more than one.</param>
/// <param name="Why">Why it isn't followed; null when it is.</param>
public sealed record XmlReach(XmlNode? Node, bool AtLeastOne, bool AtMostOne, string? Why)
{
    /// <summary>A path not followed, for <paramref name="why"/>.</summary>
    public static XmlReach Unfollowed(string why) => new(null, false, false, why);

    /// <summary>Whether it reaches a node in every instance it starts from, so that <c>exist()</c> of it is 1.</summary>
    public bool Required => Node is not null && AtLeastOne;
}
