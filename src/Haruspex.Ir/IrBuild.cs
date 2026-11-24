namespace Haruspex.Ir;

/// <summary>
/// A build of the tool that wrote a document (0.30): what made its claims. A soundness fix may change what a rule claims without
/// changing the format, so a document's version says only which properties it uses; its claims are those of this build, and a
/// reader that holds a later build can tell that the two may differ. Why:
/// docs/decisions/0117-an-ir-document-names-the-build-that-made-its-claims.md.
/// </summary>
/// <param name="Version">The tool's version, as it says it (<c>haruspex --version</c>) without the commit: <c>0.0.0-preview-0123abcd</c>.</param>
public sealed record IrBuild(string Version)
{
    /// <summary>
    /// The whole commit the build was made from, as lower-case hexadecimal digits (40, or 64 in a repository that names commits by
    /// SHA-256); null when the build didn't know it, as one made from a copy of the source without its history doesn't. A build
    /// whose commit isn't known can't be told from another of the same version, so it is never taken to be the same as one.
    /// </summary>
    public string? Commit { get; init; }

    /// <summary>
    /// Whether <paramref name="other"/> is known to make this build's claims: built from the same commit, which is one set of rules,
    /// whatever version each says (a preview and a release of one commit claim alike). Two builds whose commits aren't both known
    /// may differ, so they aren't.
    /// </summary>
    public bool IsKnownToBe(IrBuild? other) => other is { Commit: not null } && Commit == other.Commit;
}
