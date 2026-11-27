namespace Haruspex.Config;

/// <summary>What holds JSON a schema declares: what <see cref="ColumnSchema.Read"/> translates the schema's top for.</summary>
internal enum DeclaredJson
{
    /// <summary>A table's column (<c>columns</c>, or the extended property on it).</summary>
    Column,

    /// <summary>A procedure's or function's parameter (<c>parameters</c>, or the extended property on it).</summary>
    Parameter,

    /// <summary>An external REST endpoint's response (<c>endpoints</c>).</summary>
    Response,
}
