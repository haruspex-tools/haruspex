using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A synonym: another name for an object, possibly in another database or on a linked server. A write through a synonym
/// writes its object and fires that object's triggers.
/// </summary>
/// <remarks>Not a <see cref="Module"/>: it has no definition, only the name it stands for (decision 0031).</remarks>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where it is created.</param>
/// <param name="For">The name it stands for, as written.</param>
public sealed record Synonym(ObjectName Name, SourceSpan Span, SynonymTarget For) : SchemaObject(Name, Span);
