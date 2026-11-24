using System.Xml.Linq;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// Reads a table from <c>model.xml</c> into the model's <see cref="Table"/>: its columns from the table's element, and its
/// constraints and indexes from the elements that name it, which <c>model.xml</c> keeps apart from the table. Expressions (a
/// CHECK's condition, a computed column's) are parsed by ScriptDom from their scripts, as a folder's are.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a constraint was declared on.</b> DacFx links an inline constraint to the column or table it was written in by a
/// shared <c>Disambiguator</c> on their annotations, so a check written on a column is that column's
/// (<see cref="CheckConstraint.Column"/>), as in a folder.
/// </para>
/// <para>
/// <b>Which checks are trusted.</b> A check SQL Server doesn't trust guarantees nothing: one disabled (<c>IsDisabled</c>), or
/// one an extract marks not trusted, is recorded as disabled. A built package doesn't record whether a check added by
/// <c>ALTER TABLE … WITH NOCHECK ADD</c> is trusted (observed: <c>atelier</c>'s <c>ck_kiln_late</c> has no mark), so unless the
/// package is an extract a check that wasn't written inline in its table is not read at all, as a folder doesn't read one added
/// by <c>ALTER TABLE</c>; that only claims less. An extract records trust, so every check in it is read.
/// </para>
/// <para>
/// <b>Unique keys.</b> The primary key, unique constraints and unique indexes without a filter, wherever they were created: a
/// package holds a unique index made by a <c>CREATE UNIQUE INDEX</c> of its own, which a folder's reader passes over, and the
/// database has it, so the key is claimed. A disabled index's key is recorded as disabled.
/// </para>
/// </remarks>
/// <param name="package">The package's file name, for locations.</param>
/// <param name="extracted">Whether the package was extracted from a database (<see cref="DacPackage.Extracted"/>).</param>
/// <param name="diagnostics">Where an expression that doesn't parse is reported.</param>
internal sealed class TableReader(string package, bool extracted, ICollection<Diagnostic> diagnostics)
{
    /// <summary>The table, from its element and the parts that name it, in <c>model.xml</c>'s order; and the foreign keys on it with an action.</summary>
    public (Table Table, IReadOnlyList<CascadingKey> Keys) Read(XElement element, IReadOnlyList<XElement> parts)
    {
        var name = ElementName.Object(ModelXml.Named(element));
        var file = PackageLocation.File(package, name);
        var columnElements = ModelXml.Elements(element, ModelXml.Columns).ToList();
        SourceLocation At(int line) => new(file, line, 1);

        // A column's line is its place among the columns, and a part's its place after them (PackageLocation).
        var columns = new List<Column>();

        // What each inline constraint was written in: a column, by name, or the table itself, as the empty name.
        var owners = ModelXml.Disambiguators(element).Distinct(StringComparer.Ordinal).ToDictionary(d => d, _ => "", StringComparer.Ordinal);
        for (var i = 0; i < columnElements.Count; i++)
        {
            var column = columnElements[i];
            var columnName = ElementName.Last(ModelXml.Named(column));
            foreach (var disambiguator in ModelXml.Disambiguators(column))
                owners[disambiguator] = columnName;

            // A graph table's pseudo-columns ($node_id, $from_id) are listed as columns; a folder's table lists only its own,
            // and IsGraph says SELECT * has more (Table.IsGraph).
            if (ModelXml.Property(column, ModelXml.GraphType) is not null)
                continue;
            columns.Add(Column(column, columnName, At(i + 1)));
        }

        var checks = new List<CheckConstraint>();
        var keys = new List<UniqueKey>();
        var cascading = new List<CascadingKey>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var at = At(columnElements.Count + i + 1);
            switch (ModelXml.TypeOf(part))
            {
                case ModelXml.SqlCheckConstraint when Check(part, owners, at) is { } check:
                    checks.Add(check);
                    break;
                case ModelXml.SqlPrimaryKeyConstraint or ModelXml.SqlUniqueConstraint:
                    keys.Add(new UniqueKey(PartName(part), KeyColumns(part), ModelXml.TypeOf(part) == ModelXml.SqlPrimaryKeyConstraint, at));
                    break;
                case ModelXml.SqlIndex when ModelXml.IsTrue(part, ModelXml.IsUnique) && ModelXml.Property(part, ModelXml.FilterPredicate) is null:
                    // A filtered index is unique only among the rows its filter keeps.
                    keys.Add(new UniqueKey(PartName(part), KeyColumns(part), false, at) { DisabledAt = ModelXml.IsTrue(part, ModelXml.IsDisabled) ? at : null });
                    break;
                case ModelXml.SqlForeignKeyConstraint when Cascades(part) && ModelXml.References(part, ModelXml.ForeignTable).FirstOrDefault() is { } referenced:
                    cascading.Add(new CascadingKey(name, ElementName.Object(ModelXml.Named(referenced)), at));
                    break;
            }
        }

        // Column constraints first, then the table's, each in the order read: the order a folder's reader gives them.
        checks = [.. checks.Where(c => c.Column is not null), .. checks.Where(c => c.Column is null)];
        var primaryKey = keys.FirstOrDefault(k => k.IsPrimaryKey)?.Columns ?? [];
        var table = new Table(name, PackageLocation.Whole(file, columnElements.Count + parts.Count), [.. columns], primaryKey, [.. checks], [.. keys])
        {
            IsGraph = ModelXml.IsTrue(element, ModelXml.IsNode) || ModelXml.IsTrue(element, ModelXml.IsEdge),

            // Written only when on, as for a module (ModuleTextReader).
            AnsiNullsOff = !ModelXml.IsTrue(element, ModelXml.IsAnsiNullsOn),
        };
        return (table, cascading);
    }

    /// <summary>
    /// A table type, whose columns and constraints are all in its own element (<c>Columns</c> and <c>Constraints</c>), since a
    /// type can't be altered. Every constraint is part of the type and enforced, so every CHECK is read, written inline or not.
    /// </summary>
    /// <remarks>
    /// A table type's column says <c>IsNullable</c> only when it is nullable (observed in <c>till.dacpac</c>: <c>NOT NULL</c>
    /// and a primary key's column have none, a column declared <c>NULL</c> or neither has <c>True</c>), where a table's says it
    /// only when it is not; so its absence here is NOT NULL.
    /// </remarks>
    public TableType ReadType(XElement element)
    {
        var name = ElementName.Object(ModelXml.Named(element));
        var file = PackageLocation.File(package, name);
        var columnElements = ModelXml.Elements(element, ModelXml.Columns).ToList();
        var parts = ModelXml.Elements(element, ModelXml.Constraints).ToList();
        SourceLocation At(int line) => new(file, line, 1);

        var owners = ModelXml.Disambiguators(element).Distinct(StringComparer.Ordinal).ToDictionary(d => d, _ => "", StringComparer.Ordinal);
        var defaulted = parts.Where(p => ModelXml.TypeOf(p) == ModelXml.SqlTableTypeDefaultConstraint)
            .SelectMany(p => ModelXml.References(p, ModelXml.ForColumn))
            .Select(r => ElementName.Last(ModelXml.Named(r)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = new List<Column>();
        for (var i = 0; i < columnElements.Count; i++)
        {
            var column = columnElements[i];
            var columnName = ElementName.Last(ModelXml.Named(column));
            foreach (var disambiguator in ModelXml.Disambiguators(column))
                owners[disambiguator] = columnName;
            columns.Add(Column(column, columnName, At(i + 1), nullableUnlessSaid: false) with { HasDefault = defaulted.Contains(columnName) });
        }

        var checks = new List<CheckConstraint>();
        var keys = new List<UniqueKey>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var at = At(columnElements.Count + i + 1);
            switch (ModelXml.TypeOf(part))
            {
                case ModelXml.SqlTableTypeCheckConstraint when Check(part, owners, at, trusted: true) is { } check:
                    checks.Add(check);
                    break;
                case ModelXml.SqlTableTypePrimaryKeyConstraint or ModelXml.SqlTableTypeUniqueConstraint:
                case ModelXml.SqlTableTypeIndex when ModelXml.IsTrue(part, ModelXml.IsUnique) && ModelXml.Property(part, ModelXml.FilterPredicate) is null:
                    keys.Add(new UniqueKey(PartName(part), KeyColumns(part), ModelXml.TypeOf(part) == ModelXml.SqlTableTypePrimaryKeyConstraint, at)
                    {
                        IgnoresDuplicates = ModelXml.IsTrue(part, ModelXml.DoIgnoreDuplicateKey),
                    });
                    break;
            }
        }

        checks = [.. checks.Where(c => c.Column is not null), .. checks.Where(c => c.Column is null)];
        var primaryKey = keys.FirstOrDefault(k => k.IsPrimaryKey)?.Columns ?? [];
        return new TableType(name, PackageLocation.Whole(file, columnElements.Count + parts.Count), [.. columns], primaryKey, [.. checks], [.. keys]);
    }

    /// <summary>
    /// A column, of a table or of the table a function returns. Its nullability is what DacFx resolved and deploys:
    /// <c>IsNullable</c> false is NOT NULL, and its absence NULL, whether the script said NULL or nothing (an alias type's NOT NULL
    /// included). A computed column takes its nullability from its expression, as in a folder, unless it is declared
    /// <c>PERSISTED NOT NULL</c> (<c>IsPersistedNullable</c> false) or <c>PERSISTED NULL</c> (true). A masked column carries its function
    /// as written, in <c>MaskingFunction</c> (<c>default()</c>).
    /// </summary>
    /// <param name="column">The column's element.</param>
    /// <param name="name">Its name.</param>
    /// <param name="at">Where it is read.</param>
    /// <param name="nullableUnlessSaid">Whether a column without <c>IsNullable</c> is nullable: a table's is, a table type's isn't.</param>
    public Column Column(XElement column, string name, SourceLocation at, bool nullableUnlessSaid = true)
    {
        if (ModelXml.TypeOf(column) is ModelXml.SqlComputedColumn or ModelXml.SqlTableTypeComputedColumn)
        {
            var expression = Parse(ModelXml.Property(column, ModelXml.ExpressionScript) ?? "", at, (parser, reader) => (parser.ParseExpression(reader, out var errors), errors));
            var declared = ModelXml.Property(column, ModelXml.IsPersistedNullable) switch
            {
                "False" => DeclaredNullability.NotNull,
                "True" => DeclaredNullability.Null,
                _ => DeclaredNullability.NotDeclared,
            };
            return new Column(name, null, declared, false, expression, at);
        }

        var nullable = ModelXml.Property(column, ModelXml.IsNullable) is { } said ? said == "True" : nullableUnlessSaid;
        var nullability = nullable ? DeclaredNullability.Null : DeclaredNullability.NotNull;
        return new Column(name, TypeSpecifier.Of(column, ModelXml.TypeSpecifier), nullability, ModelXml.IsTrue(column, ModelXml.IsIdentity), null, at, ModelXml.Property(column, ModelXml.Collation))
        {
            IsHidden = ModelXml.IsTrue(column, ModelXml.IsHidden),
            IsSparse = ModelXml.IsTrue(column, ModelXml.IsSparse),
            IsColumnSet = ModelXml.TypeOf(column) == ModelXml.SqlColumnSet,
            Mask = ModelXml.Property(column, ModelXml.MaskingFunction),
        };
    }

    /// <summary>
    /// A CHECK constraint, or null for one a build doesn't say is trusted (see the remarks). It was written inline when it shares
    /// a <c>Disambiguator</c> with its table or one of its columns, whichever of the two carries the annotation and which the
    /// attached one (observed: an unnamed check carries the annotation, a named one the attached one).
    /// </summary>
    private CheckConstraint? Check(XElement check, Dictionary<string, string> owners, SourceLocation at, bool trusted = false)
    {
        var owner = ModelXml.Disambiguators(check).Select(d => owners.GetValueOrDefault(d)).FirstOrDefault(o => o is not null);
        if (!extracted && !trusted && owner is null)
            return null;
        if (Parse(ModelXml.Property(check, ModelXml.CheckExpressionScript) ?? "", at, (parser, reader) => (parser.ParseBooleanExpression(reader, out var errors), errors)) is not { } condition)
            return null;
        var column = owner is { Length: > 0 } ? owner : null;
        var enforced = !ModelXml.IsTrue(check, ModelXml.IsDisabled) && ModelXml.Annotation(check, ModelXml.IsNotTrustedPropertyAnnotation) is null;
        return new CheckConstraint(PartName(check), condition, column, ModelXml.IsTrue(check, ModelXml.IsNotForReplication), at, enforced ? null : at);
    }

    /// <summary>An expression's script parsed so that it starts at <paramref name="at"/>; null, reported, when it doesn't parse.</summary>
    private T? Parse<T>(string script, SourceLocation at, Func<TSqlParser, TextReader, (T? Fragment, IList<ParseError> Errors)> parse)
        where T : TSqlFragment
    {
        using var reader = new StringReader(new string('\n', at.Line - 1) + script);
        var (fragment, errors) = parse(new TSql170Parser(initialQuotedIdentifiers: true), reader);
        foreach (var error in errors)
            diagnostics.Add(new Diagnostic(SourceCodes.ParseError, error.Message, new SourceLocation(at.File, error.Line, error.Column)));
        return errors.Count == 0 ? fragment : null;
    }

    /// <summary>A constraint's or index's own name, or null for one declared without a name.</summary>
    private static string? PartName(XElement part) => ModelXml.NameOf(part) is { } name ? ElementName.Last(name) : null;

    /// <summary>The columns of a key or index, in key order.</summary>
    private static Collections.EquatableArray<string> KeyColumns(XElement part) =>
        [.. ModelXml.Elements(part, ModelXml.ColumnSpecifications)
            .SelectMany(spec => ModelXml.References(spec, ModelXml.Column))
            .Select(reference => ElementName.Last(ModelXml.Named(reference)))];

    /// <summary>Whether a foreign key has an action on delete or update, so that a change to its referenced table changes this one.</summary>
    private static bool Cascades(XElement key) =>
        (ModelXml.Property(key, ModelXml.OnDeleteAction) is { } delete && delete != "0")
        || (ModelXml.Property(key, ModelXml.OnUpdateAction) is { } update && update != "0");
}
