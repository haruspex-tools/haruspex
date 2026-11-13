using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// An object of the schema with a name of its own: a table, a type, a synonym, or a module (a procedure, function, view or
/// trigger). Each knows its name and where it was read.
/// </summary>
/// <remarks>
/// <para>
/// One kind is read for more than the schema: a table a body declares (<c>CREATE TABLE #t</c>, <c>DECLARE @t TABLE</c>) or a
/// multi-statement function returns is read as a <see cref="Table"/> too, named by itself with no schema
/// (<c>ObjectName("", "#t")</c>), so that the schema's rules (which columns are never null, what a CHECK constraint allows,
/// which keys hold) apply to it unchanged. It is never in <see cref="DatabaseModel"/>, and it is not a schema object; why it
/// isn't a type of its own yet is in decision 0031.
/// </para>
/// <para>
/// A closed hierarchy: its constructor is not visible outside this assembly, so the kinds below are all there are, and a
/// reader can match on them knowing none is missing. Why the model uses closed hierarchies of records for what is one of
/// several, rather than a kind and fields that are null for the other kinds, is in decision 0031
/// (<c>docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md</c>).
/// </para>
/// </remarks>
public abstract record SchemaObject
{
    /// <summary>Creates an object; only the kinds in this assembly can.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="span">Where its definition is.</param>
    private protected SchemaObject(ObjectName name, SourceSpan span)
    {
        Name = name;
        Span = span ?? throw new ArgumentNullException(nameof(span));
    }

    /// <summary>The object's name.</summary>
    public ObjectName Name { get; }

    /// <summary>Where its definition is; every position inside it is in this span's file.</summary>
    public SourceSpan Span { get; }

    /// <summary>The object's name.</summary>
    public sealed override string ToString() => Name.ToString();
}
