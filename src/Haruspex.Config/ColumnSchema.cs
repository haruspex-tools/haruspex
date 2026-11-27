using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// A column's JSON Schema, named as <c>file#pointer</c> relative to the config file's folder, read and translated into a
/// <see cref="ColumnDeclaration"/>: the same for a declaration in the config file (<c>columns</c>) and for one in the SQL (an
/// extended property, <see cref="ColumnAnnotations"/>), so that the two declare a column alike
/// (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md,
/// docs/decisions/0141-a-column-s-json-schema-may-be-named-in-the-sql.md).
/// </summary>
internal static class ColumnSchema
{
    /// <summary>The declaration <paramref name="reference"/> makes; null when it has a problem, reported at <paramref name="at"/>.</summary>
    /// <param name="files">The schema files read so far, which reports a file it can't read.</param>
    /// <param name="directory">The config file's folder, which the reference is relative to.</param>
    /// <param name="key">The column as the IR records it, <c>schema.table.column</c>.</param>
    /// <param name="table">The table.</param>
    /// <param name="column">The column's name.</param>
    /// <param name="reference">The schema's file, relative to <paramref name="directory"/>, then <c>#</c> and a JSON Pointer inside it when it isn't the whole file.</param>
    /// <param name="at">Where the reference is written: a problem with it, rather than inside the schema, is reported there.</param>
    /// <param name="problems">Where every problem is reported.</param>
    public static ColumnDeclaration? Declare(SchemaFiles files, string directory, string key, ObjectName table, string column, string reference, DiagnosticLocation at, List<Diagnostic> problems) =>
        Read(files, directory, key, reference, at, problems, response: false) is var (content, types, schema, pointer, hashes)
            ? new ColumnDeclaration(table, column, content, types, new IrColumnDeclaration(key, schema, pointer, hashes), at)
            : null;

    /// <summary>
    /// The JSON a schema named by <paramref name="reference"/> describes, read as a column's JSON is, whose writer isn't the database:
    /// an object may hold keys it doesn't list, and a key it doesn't require may be left out. Shared with the response declared for
    /// an external REST endpoint (<c>endpoints</c>, docs/decisions/0140-a-rest-response-is-typed-from-a-schema-declared-for-its-endpoint.md),
    /// whose top may allow <c>null</c>, which never arrives; a column's may not. Null when it has a problem, reported at <paramref name="at"/>.
    /// </summary>
    /// <param name="key">What is declared, which names the object types the schema defines.</param>
    /// <param name="response">Whether it is an endpoint's response rather than a column's JSON.</param>
    /// <returns>The JSON's type, the object types it refers to, the schema file as recorded, the pointer, and every file's hash.</returns>
    public static (IrType Content, EquatableMap<string, IrObjectType> Types, string Schema, string? Pointer, EquatableMap<string, string> Files)? Read(
        SchemaFiles files, string directory, string key, string reference, DiagnosticLocation at, List<Diagnostic> problems, bool response)
    {
        var hash = reference.IndexOf('#', StringComparison.Ordinal);
        var schemaPath = hash < 0 ? reference : reference[..hash];
        var pointer = hash < 0 ? null : reference[(hash + 1)..];
        var errors = problems.Count(p => p.IsError);
        if (DeclaredSchema.IsRemote(schemaPath))
        {
            problems.Add(ConfigCodes.ForeignReference.At(at, DeclaredSchema.Remote(schemaPath)));
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(directory, schemaPath));
        if (files.Load(full, at, schemaPath) is not { } document)
            return null;
        if (JsonPointer.Resolve(document.Root, "", pointer ?? "") is not var (root, rootPath))
        {
            problems.Add(ConfigCodes.MissingReference.At(at, $"points at \"{pointer}\" in {schemaPath}, which names nothing: a pointer is a JSON Pointer, such as #/$defs/person."));
            return null;
        }

        var types = new Dictionary<string, IrObjectType>(StringComparer.Ordinal);
        var translator = DeclaredSchema.ForColumn(document, (root, rootPath), problems, files, key, types);
        var content = response ? translator.Response() : translator.Column();
        if (content is null || problems.Count(p => p.IsError) != errors)
            return null;
        return (
            content,
            types.ToEquatableMap(t => t.Key, t => t.Value),
            files.Relative(full),
            string.IsNullOrEmpty(pointer) ? null : pointer,
            translator.Files.ToEquatableMap(files.Relative, files.Sha256));
    }
}
