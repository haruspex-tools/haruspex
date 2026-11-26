using System.Globalization;
using System.Text.Json;
using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// Reads an IR file into an <see cref="IrDocument"/>: the inverse of <see cref="IrJsonWriter"/>, so that what a consumer of
/// the file gets is what haruspex inferred. Reading what the writer wrote and writing it again gives the same bytes, and the
/// document read equals the one written; the tests hold every fixture and both public corpora to that round trip.
/// </summary>
/// <remarks>
/// <para>
/// The reader is as strict as the format is closed. It refuses, with an <see cref="IrFormatException"/> that names the path
/// in the document: text that is not JSON; a version other than those in <see cref="IrDocument.ReadableVersions"/>; a property missing,
/// of the wrong type, unknown or given twice; a name outside its vocabulary; a negative count; a flag that disagrees with
/// its reasons (<c>nullable</c> without <c>nullableBecause</c>); and a reference that names no type. It supplies no default: a
/// property is either in the file or, where the format lets the writer leave it out, absent with the meaning the format gives
/// that.
/// </para>
/// <para>
/// A version it doesn't read is refused before anything else, rather than read with its differences guessed at: the IR has
/// no stable version yet (0.x), so a file from another version is regenerated, not migrated. It reads the current version and
/// each older one every document of which is a document of the current version that means the same
/// (<see cref="IrDocument.ReadableVersions"/>), and holds each document's label to what it says (<see cref="IrDocument.Version"/>).
/// Why: docs/decisions/0024-an-ir-reader-reads-only-versions-whose-documents-mean-what-its-own-do.md.
/// </para>
/// </remarks>
public static class IrJsonReader
{
    /// <summary>The deepest nesting read, as deep as the writer's own limit, so that every file the writer writes is read.</summary>
    private const int MaxDepth = 1000;

    /// <summary>The document an IR file holds.</summary>
    /// <param name="json">The file's text.</param>
    /// <exception cref="IrFormatException">The text is not an IR document of this version; the message says where and why.</exception>
    public static IrDocument Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException e)
        {
            // The parser counts lines and bytes from 0; a person counts from 1.
            var where = e.LineNumber is { } line && e.BytePositionInLine is { } position
                ? $" at line {(line + 1).ToString(CultureInfo.InvariantCulture)}, byte {(position + 1).ToString(CultureInfo.InvariantCulture)}"
                : "";
            throw new IrFormatException(e.Path ?? "$", $"is not JSON{where}: {e.Message}", e);
        }

        using (parsed)
        {
            var document = Document(IrJsonObject.At(parsed.RootElement, "$"));
            RequireNamedOnce(document);
            RequireReferencesResolve(document);
            RequireFoldsResolve(document);
            RequireTableTypesResolve(document);
            return document;
        }
    }

    /// <summary>
    /// What is looked up by its name is named once: an object by its id, a type's field and an object's parameter by their names.
    /// Every program that compares, fits or combines documents finds an object by its id and a field by its name, so a document
    /// that gave two would be read as whichever it found, and haruspex's own would stop with an error of its own rather than the
    /// file's. JSON Schema can't say this: it can't compare one item's property with another's.
    /// </summary>
    private static void RequireNamedOnce(IrDocument document)
    {
        if (Twice(document.Objects, o => o.Id) is { } obj)
            throw new IrFormatException($"$.objects[{obj}].id", $"is \"{document.Objects[obj].Id}\", an id an earlier object has: each object's id is its own.");
        foreach (var (o, i) in document.Objects.Select((o, i) => (o, i)))
        {
            if (Twice(o.Parameters, p => p.Name) is { } parameter)
                throw new IrFormatException($"$.objects[{i}].parameters[{parameter}].name", $"is \"{o.Parameters[parameter].Name}\", the name of an earlier parameter: each parameter's name is its own.");
        }

        foreach (var (name, type) in document.Types)
        {
            if (Twice(type.Fields, f => f.Name) is { } field)
                throw new IrFormatException($"$.types{IrPath.Key(name)}.fields[{field}].name", $"is \"{type.Fields[field].Name}\", the name of an earlier field: a type's fields are named apart.");
        }

        // The position of the first item whose key an earlier item has, or null.
        static int? Twice<T>(IReadOnlyList<T> items, Func<T, string> key)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < items.Count; i++)
            {
                if (!seen.Add(key(items[i])))
                    return i;
            }

            return null;
        }
    }

    /// <summary>
    /// Every reference names a type in <c>types</c>: a dangling one would leave a reader with a value of no type at all. JSON
    /// Schema can't say this (it can't compare a value with a key elsewhere in the document), so the reader does. The references
    /// are found in the document read, not by searching the file for a key named <c>ref</c>: a map keyed by data, such as a
    /// declaration's <c>files</c>, may have a key of that name.
    /// </summary>
    private static void RequireReferencesResolve(IrDocument document)
    {
        foreach (var (path, reference) in IrDocumentScalars.References(document))
        {
            if (!document.Types.ContainsKey(reference.Name))
                throw new IrFormatException(path + ".ref", $"is \"{reference.Name}\", which names no type in $.types.");
        }
    }

    /// <summary>Every value set's fold names a fold of the document, found as the references are, in the document read.</summary>
    private static void RequireFoldsResolve(IrDocument document)
    {
        foreach (var (path, scalar) in IrDocumentScalars.Of(document))
        {
            if (scalar.Facets?.Values?.Fold is { } name && !document.Folds.ContainsKey(name))
                throw new IrFormatException(path + ".values.fold", $"is \"{name}\", which names no fold in $.folds.");
        }
    }

    /// <summary>
    /// The version a file says it is (<c>irVersion</c>), read without the rest of it, so that a program can tell a file of another
    /// version from one that is broken before it reads either. Null when the text isn't JSON, its root isn't an object, or it has
    /// no <c>irVersion</c> that is a string of text.
    /// </summary>
    /// <param name="json">The file's text.</param>
    public static string? VersionOf(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            return parsed.RootElement.ValueKind == JsonValueKind.Object
                && parsed.RootElement.TryGetProperty("irVersion", out var version)
                && version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // A key or the version that escapes half of a surrogate pair alone: not text, so no version; Read says where.
            return null;
        }
    }

    private static IrDocument Document(IrJsonObject json)
    {
        // The version first: a file of another version may differ anywhere, so nothing else in it is read.
        var version = json.Optional("irVersion") is { } found
            ? IrJsonObject.AsString(found, json.PathOf("irVersion"))
            : throw new IrFormatException(json.PathOf("irVersion"), "is missing, so this is not a haruspex IR file.");
        if (!IrDocument.ReadableVersions.Contains(version, StringComparer.Ordinal))
        {
            throw new IrFormatException(
                json.PathOf("irVersion"),
                $"is \"{version}\", and this reader reads IR {string.Join(", ", IrDocument.ReadableVersions.SkipLast(1))} and {IrDocument.ReadableVersions[^1]} only. Run haruspex generate again to write the file in this version.");
        }

        // A document is labelled by what it says (IrDocument.Version), so a label that disagrees with it is refused by the path
        // of what disagrees.
        var source = json.Object("source");
        var baseline = source.OptionalObject("baseline");
        if (baseline is not null && version == IrDocument.OneDatabaseVersion)
            throw new IrFormatException(baseline.Path, $"is given in a document of IR {version}, which describes one database: a document combining two is IR {IrDocument.CombinedVersion} or later.");
        if (baseline is null && version == IrDocument.CombinedVersion)
            throw new IrFormatException(source.PathOf("baseline"), $"is missing: a document of IR {version} combines two databases and names the other's source. A document of one database is IR {IrDocument.OneDatabaseVersion}, {IrDocument.SourcesVersion} or later.");
        var tableTypes = json.OptionalObject("tableTypes");
        if (tableTypes is not null && !IrDocument.IsSince(version, IrDocument.TableTypesVersion))
            throw new IrFormatException(tableTypes.Path, $"is given in a document of IR {version}: a document that defines a table type is IR {IrDocument.TableTypesVersion}, {IrDocument.BytesVersion} when it holds a binary value, or {IrDocument.DeclaredVersion} or later when it declares a column's JSON.");
        if (tableTypes is null && version == IrDocument.TableTypesVersion)
            throw new IrFormatException(json.PathOf("tableTypes"), $"is missing: a document of IR {version} defines the table types its parameters take. One that defines none is labelled {IrDocument.SourcesVersion}, {IrDocument.CombinedVersion} or {IrDocument.OneDatabaseVersion}.");

        var document = new IrDocument(
            DocumentSource(source) with { Baseline = baseline is null ? null : Baseline(baseline) },
            [.. json.Array("objects").Select(o => Object(IrJsonObject.At(o.Item, o.Path)))],
            Types(json.Object("types")),
            [.. json.Array("unknowns").Select(u => Unknown(IrJsonObject.At(u.Item, u.Path)))])
        {
            Overrides = [.. json.Array("overrides").Select(o => Override(IrJsonObject.At(o.Item, o.Path)))],
            Folds = Folds(json.Object("folds")),
            TableTypes = tableTypes is null ? [] : TableTypes(tableTypes),
            Columns = Columns(json),
        };
        source.End();
        json.End();
        if (baseline is null)
            OneDatabase(document, version);
        if (!IrDocument.IsSince(version, IrDocument.SourcesVersion))
            SaysNothingOfSources(document, version);
        if (version == IrDocument.SourcesVersion && !document.SaysSources)
            throw new IrFormatException("$.irVersion", $"is \"{version}\", which labels a document that says where a field's value came from or where its source was read, and this one says neither. One that says neither is labelled {IrDocument.CombinedVersion} or {IrDocument.OneDatabaseVersion}, by whether it combines two databases.");
        HoldsBytesAsLabelled(document, version);
        DeclaresAsLabelled(document, version);
        ExplainsAsLabelled(document, version);
        NamesItsWriterAsLabelled(document, version);
        DeclaresBuildsAsLabelled(document, version);
        ReadsFoldersAsLabelled(document, version);
        return document;
    }

    /// <summary>
    /// The columns declared to hold JSON of a schema (0.28); none when the file leaves <c>columns</c> out. An empty array is
    /// refused: the writer leaves it out, so there is one way to say "none". Each column is declared once.
    /// </summary>
    private static EquatableArray<IrColumnDeclaration> Columns(IrJsonObject json)
    {
        if (json.Optional("columns") is null)
            return [];
        var columns = new List<IrColumnDeclaration>();
        foreach (var (item, path) in json.Array("columns"))
        {
            var entry = IrJsonObject.At(item, path);
            var files = entry.Object("files");
            var column = new IrColumnDeclaration(entry.String("column"), entry.String("schema"), entry.OptionalString("pointer"), files.Names.ToList().ToEquatableMap(name => name, files.Sha256));
            files.End();
            entry.End();
            if (!column.Files.ContainsKey(column.Schema))
                throw new IrFormatException(entry.PathOf("files"), $"has no hash of \"{column.Schema}\", the schema file itself: it lists every file the declaration read.");
            if (columns.Any(c => c.Column == column.Column))
                throw new IrFormatException(entry.PathOf("column"), $"is \"{column.Column}\", declared twice: a column is declared once.");
            columns.Add(column);
        }

        return columns.Count > 0 ? [.. columns] : throw new IrFormatException(json.PathOf("columns"), "is empty: a document that declares no column leaves it out.");
    }

    /// <summary>
    /// Refuses what only a declaration of a column's JSON gives (0.28) in a document labelled before 0.28, by its path, and a document
    /// labelled 0.28 that says nothing of the kind, which is labelled by the rest of what it says (<see cref="IrDocument.Version"/>).
    /// A claim that an object reads a declared column must name one the document declares.
    /// </summary>
    private static void DeclaresAsLabelled(IrDocument document, string version)
    {
        foreach (var (claim, i) in IrDocumentOrder.Overrides(document).Select((o, i) => (o, i)))
        {
            if (claim.Setting == IrOverrideSetting.ColumnJson && !document.Columns.Any(c => c.Column == claim.Via))
                throw new IrFormatException($"$.overrides[{i}].via", $"is {(claim.Via is null ? "missing" : $"\"{claim.Via}\"")}, but a \"column-json\" claim names a column the document declares in $.columns.");
        }

        if (version == IrDocument.DeclaredVersion && !document.Declares)
            throw new IrFormatException("$.irVersion", $"is \"{version}\", which labels a document that declares a column's JSON, and this one declares none. One that declares none is labelled {IrDocument.BytesVersion} or earlier, by what else it says.");
        if (IrDocument.IsSince(version, IrDocument.DeclaredVersion))
            return;
        if (document.Columns.Count > 0)
            throw new IrFormatException("$.columns", $"is given in a document of IR {version}: a document that declares a column's JSON is IR {IrDocument.DeclaredVersion} or later.");
        if (IrDocumentDeclared.First(document) is var (path, what))
            throw new IrFormatException(path, $"is {what}, given in a document of IR {version}: only a declaration gives it, in IR {IrDocument.DeclaredVersion} or later.");
    }

    /// <summary>
    /// Refuses a reason's detail or a minimum length's unit (0.29) in a document labelled before 0.29, by its path, and a document
    /// labelled 0.29 that has neither, which is labelled by the rest of what it says (<see cref="IrDocument.Version"/>).
    /// </summary>
    private static void ExplainsAsLabelled(IrDocument document, string version)
    {
        var first = IrDocumentExplained.First(document);
        if (version == IrDocument.ExplainedVersion && first is null)
            throw new IrFormatException("$.irVersion", $"is \"{version}\", which labels a document in which a reason says what makes it so or a minimum length is in code points, and this one has neither. One that has neither is labelled {IrDocument.DeclaredVersion} or earlier, by what else it says.");
        if (!IrDocument.IsSince(version, IrDocument.ExplainedVersion) && first is { } path)
            throw new IrFormatException(path, $"is given in a document of IR {version}: a reason's detail and a minimum length's unit are IR {IrDocument.ExplainedVersion} or later.");
    }

    /// <summary>
    /// Every table type a parameter or a type's identity names is defined in <c>tableTypes</c>, and none is defined that nothing
    /// names, as for <c>types</c>: a parameter whose definition is missing can't be sent or compared.
    /// </summary>
    private static void RequireTableTypesResolve(IrDocument document)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (obj, i) in document.Objects.Select((o, i) => (o, i)))
        {
            foreach (var (parameter, j) in obj.Parameters.Select((p, j) => (p, j)))
            {
                var at = $"$.objects[{i}].parameters[{j}]";
                if (parameter.TableType is { } key && !document.TableTypes.ContainsKey(key))
                    throw new IrFormatException(at + ".tableType", $"is \"{key}\", which names no table type in $.tableTypes.");
                if (parameter.TableType is not null)
                    named.Add(parameter.TableType);

                // A parameter refers to a named type only as a table-valued one does, to its type's rows.
                var rows = parameter.Type is IrArray { Items: IrRef row } && document.Types.TryGetValue(row.Name, out var type) && type.Identity is { Owner: IrTypeOwner.TableType } identity
                    && identity.Object == parameter.TableType;
                if (IrTypeTree.References(parameter.Type).Any() && !rows)
                    throw new IrFormatException(at + ".type", $"refers to a named type, which only a table-valued parameter does: an array of the rows of the table type its tableType names.");
            }
        }

        foreach (var (name, type) in document.Types)
        {
            if (type.Identity is not { Owner: IrTypeOwner.TableType, Object: var key })
                continue;
            if (!document.TableTypes.ContainsKey(key))
                throw new IrFormatException($"$.types{IrPath.Key(name)}.identity.tableType", $"is \"{key}\", which names no table type in $.tableTypes.");
            if (type.Fields.Select((f, i) => (f, i)).FirstOrDefault(p => p.f.Type is not IrScalar) is ({ } field, var i))
                throw new IrFormatException($"$.types{IrPath.Key(name)}.fields[{i}].type", $"is not a scalar, but {field.Name} is a column of a table type's row, which is one.");
        }

        if (document.TableTypes.Keys.FirstOrDefault(k => !named.Contains(k)) is { } unused)
            throw new IrFormatException("$.tableTypes" + IrPath.Key(unused), "is a table type no parameter takes; a document defines only those its parameters take.");
    }

    private static EquatableMap<string, IrTableType> TableTypes(IrJsonObject json)
    {
        var types = json.Names.ToList().ToEquatableMap(name => name, name => TableType(json.Object(name)));
        json.End();
        return types;
    }

    private static IrTableType TableType(IrJsonObject json)
    {
        var type = new IrTableType(
            json.String("schema"),
            json.String("name"),
            [.. json.Array("columns").Select(c => TableColumn(IrJsonObject.At(c.Item, c.Path)))],
            json.String("at"))
        {
            Keys = [.. json.Array("keys").Select(k => TableKey(IrJsonObject.At(k.Item, k.Path)))],
            Checks = [.. json.Array("checks").Select(c => IrJsonObject.AsString(c.Item, c.Path))],
        };
        if (type.Columns.Count == 0)
            throw new IrFormatException(json.PathOf("columns"), "is empty, but a table type has at least one column.");
        var names = type.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count != type.Columns.Count)
            throw new IrFormatException(json.PathOf("columns"), "names a column twice; a table type's columns are named apart, ignoring case.");
        foreach (var (key, i) in type.Keys.Select((k, i) => (k, i)))
        {
            if (key.Columns.Count == 0 || key.Columns.Any(c => !names.Contains(c)))
                throw new IrFormatException($"{json.PathOf("keys")}[{i}].columns", "names no column, or a column the type doesn't have.");
        }

        json.End();
        return type;
    }

    private static IrTableColumn TableColumn(IrJsonObject json)
    {
        var column = new IrTableColumn(json.String("name"), json.OptionalString("sql"), json.Boolean("nullable"))
        {
            Type = json.OptionalObject("type") is { } type ? Type(type) as IrScalar ?? throw new IrFormatException(type.Path, "is not a scalar; a table type's column is one, or leaves its type out.") : null,
            Generated = json.OptionalName<IrColumnGeneration>("generated", IrWireNames.TryParse, "how a column is filled"),
            Collation = json.OptionalString("collation"),
        };
        if (column.Sql is null && column.Generated != IrColumnGeneration.Computed)
            throw new IrFormatException(json.PathOf("sql"), "is missing: only a computed column declares no type.");
        json.End();
        return column;
    }

    private static IrTableKey TableKey(IrJsonObject json)
    {
        var key = new IrTableKey([.. json.Array("columns").Select(c => IrJsonObject.AsString(c.Item, c.Path))])
        {
            IgnoresDuplicates = json.OptionalBoolean("ignoresDuplicates") ?? false,
        };
        json.End();
        return key;
    }

    /// <summary>A source's own properties: its dialect, its package and references, the packages declared builds, and the callers' session.</summary>
    private static IrSource DocumentSource(IrJsonObject json)
    {
        var source = new IrSource(json.String("dialect"))
        {
            WrittenBy = json.OptionalObject("writtenBy") is { } build ? Build(build) : null,
            Path = json.OptionalString("path"),
            Paths = Paths(json),
            Session = Session(json.OptionalObject("session")),
            Files = Files(json.OptionalObject("files")),
            Package = Package(json),
            References = References(json),
        };
        return source with { Builds = Builds(json, source) };
    }

    /// <summary>The build that wrote a source's claims (0.30): its version, never empty, and its commit when it knew it.</summary>
    private static IrBuild Build(IrJsonObject json)
    {
        var build = new IrBuild(json.NonEmptyString("version")) { Commit = json.OptionalCommit("commit") };
        json.End();
        return build;
    }

    /// <summary>
    /// Refuses a document labelled 0.30 or later that doesn't name the build that wrote it, and the build named in a document labelled
    /// earlier, its own or its baseline's, by its path: a document of 0.30 or later always names its writer, and one labelled earlier
    /// names none.
    /// </summary>
    private static void NamesItsWriterAsLabelled(IrDocument document, string version)
    {
        if (IrDocument.IsSince(version, IrDocument.StampedVersion) && document.Source.WrittenBy is null)
            throw new IrFormatException("$.source.writtenBy", $"is missing: a document of IR {version} names the build that wrote it. One labelled before {IrDocument.StampedVersion} names none, and is labelled by what else it says.");
        if (IrDocument.IsSince(version, IrDocument.StampedVersion))
            return;
        if (document.Source.WrittenBy is not null)
            throw new IrFormatException("$.source.writtenBy", $"is given in a document of IR {version}: the build that wrote a document is IR {IrDocument.StampedVersion} or later.");
        if (document.Source.Baseline?.WrittenBy is not null)
            throw new IrFormatException("$.source.baseline.writtenBy", $"is given in a document of IR {version}: the build that wrote a document is IR {IrDocument.StampedVersion} or later.");
    }

    /// <summary>
    /// Refuses packages declared builds (0.31) in a document labelled earlier, its own source's or its baseline's, by their path, and
    /// a document labelled 0.31 that declares none, which is labelled by the rest of what it says (<see cref="IrDocument.Version"/>).
    /// </summary>
    private static void DeclaresBuildsAsLabelled(IrDocument document, string version)
    {
        if (version == IrDocument.BuildsVersion && !document.DeclaresBuilds)
            throw new IrFormatException("$.irVersion", $"is \"{version}\", which labels a document that records packages declared builds, and this one records none. One that records none is labelled {IrDocument.StampedVersion} or earlier, by what else it says.");
        if (IrDocument.IsSince(version, IrDocument.BuildsVersion))
            return;
        if (document.Source.Builds.Count > 0)
            throw new IrFormatException("$.source.builds", $"is given in a document of IR {version}: packages declared builds are IR {IrDocument.BuildsVersion} or later.");
        if (document.Source.Baseline?.Builds.Count > 0)
            throw new IrFormatException("$.source.baseline.builds", $"is given in a document of IR {version}: packages declared builds are IR {IrDocument.BuildsVersion} or later.");
    }

    /// <summary>
    /// Refuses a source read from several folders (0.32) in a document labelled earlier, its own or its baseline's, by its path, and
    /// a document labelled 0.32 whose sources were each read from one place, which is labelled by the rest of what it says
    /// (<see cref="IrDocument.Version"/>).
    /// </summary>
    private static void ReadsFoldersAsLabelled(IrDocument document, string version)
    {
        if (version == IrDocument.CurrentVersion && !document.ReadsFolders)
            throw new IrFormatException("$.irVersion", $"is \"{version}\", which labels a document whose source was read from several folders, and this one records no such source. One that records none is labelled {IrDocument.BuildsVersion} or earlier, by what else it says.");
        if (version == IrDocument.CurrentVersion)
            return;
        if (document.Source.Paths.Count > 0)
            throw new IrFormatException("$.source.paths", $"is given in a document of IR {version}: a source read from several folders is IR {IrDocument.CurrentVersion}.");
        if (document.Source.Baseline?.Paths.Count > 0)
            throw new IrFormatException("$.source.baseline.paths", $"is given in a document of IR {version}: a source read from several folders is IR {IrDocument.CurrentVersion}.");
    }

    /// <summary>The source of the other database a combined document describes, which has nothing more.</summary>
    private static IrSource Baseline(IrJsonObject json)
    {
        var source = DocumentSource(json);
        json.End();
        return source;
    }

    /// <summary>
    /// Refuses a binary value (0.27) in a document labelled before 0.27, by the path of the first, and a document labelled 0.27 that
    /// holds none, which is labelled by the rest of what it says (<see cref="IrDocument.Version"/>).
    /// </summary>
    private static void HoldsBytesAsLabelled(IrDocument document, string version)
    {
        var first = IrDocumentScalars.Of(document).FirstOrDefault(s => s.Scalar.Kind == IrScalarKind.Bytes);
        if (first.Scalar is not null && !IrDocument.IsSince(version, IrDocument.BytesVersion))
            throw new IrFormatException(first.Path, $"is a binary value (kind \"bytes\"), given in a document of IR {version}: a document that holds one is IR {IrDocument.BytesVersion}, or {IrDocument.DeclaredVersion} or later when it declares a column's JSON.");
        if (first.Scalar is null && version == IrDocument.BytesVersion)
            throw new IrFormatException("$.irVersion", $"is \"{version}\", which labels a document that holds a binary value, and this one holds none. One that holds none is labelled {IrDocument.TableTypesVersion} or earlier, by what else it says.");
    }

    /// <summary>
    /// Refuses, by its path, where a field's value came from (0.25) in a document labelled 0.23 or 0.24, which can't say it.
    /// </summary>
    private static void SaysNothingOfSources(IrDocument document, string version)
    {
        if (document.Source.Path is not null)
            throw new IrFormatException("$.source.path", $"is given in a document of IR {version}: a source's path is IR {IrDocument.SourcesVersion}.");
        if (document.Source.Baseline?.Path is not null)
            throw new IrFormatException("$.source.baseline.path", $"is given in a document of IR {version}: a source's path is IR {IrDocument.SourcesVersion}.");
        if (document.Source.Files.Count > 0)
            throw new IrFormatException("$.source.files", $"is given in a document of IR {version}: a source's files are IR {IrDocument.SourcesVersion}.");
        if (document.Source.Baseline?.Files.Count > 0)
            throw new IrFormatException("$.source.baseline.files", $"is given in a document of IR {version}: a source's files are IR {IrDocument.SourcesVersion}.");
        foreach (var (name, type) in document.Types)
        {
            foreach (var (field, i) in type.Fields.Select((f, i) => (f, i)))
            {
                var at = $"$.types{IrPath.Key(name)}.fields[{i}]";
                if (field.From.Count > 0)
                    throw new IrFormatException(at + ".from", $"is given in a document of IR {version}: a field's source columns are IR {IrDocument.SourcesVersion}.");
                if (field.Sides is not null)
                    throw new IrFormatException(at + ".sides", $"is given in a document of IR {version}: a combined field's sides are IR {IrDocument.SourcesVersion}.");
            }
        }
    }

    /// <summary>
    /// Refuses, by its path, what only a document combining two databases says (0.24) in one of one database, which could say
    /// neither: a key that may be left out whatever its value, and a result missing because only one database returns it.
    /// </summary>
    /// <remarks>
    /// From 0.28 a key may be left out whatever its value in a document of one database too, where a column's JSON is declared: a key
    /// a schema does not require is one an application may leave out or write as null (<see cref="IrPresence.Optional"/>).
    /// </remarks>
    private static void OneDatabase(IrDocument document, string version)
    {
        const string Why = "which only a document combining two databases (IR 0.24 or later) says";
        foreach (var (name, type) in document.Types)
        {
            foreach (var (field, i) in type.Fields.Select((f, i) => (f, i)))
            {
                var at = $"$.types{IrPath.Key(name)}.fields[{i}]";
                if (field.Presence == IrPresence.Optional && !IrDocument.IsSince(version, IrDocument.DeclaredVersion))
                    throw new IrFormatException(at + ".presence", $"is \"{IrWireNames.Of(field.Presence)}\", which only a document combining two databases (IR 0.24 or later), or one declaring a column's JSON (IR {IrDocument.DeclaredVersion} or later), says.");
                if (field.Sides is not null)
                    throw new IrFormatException(at + ".sides", $"is given, {Why}.");
                Rules(at + ".nullableBecause", field.NullableBecause);
            }
        }

        foreach (var (obj, i) in document.Objects.Select((o, i) => (o, i)))
        {
            foreach (var (result, j) in (obj.Results ?? []).Select((r, j) => (r, j)))
                Rules($"$.objects[{i}].results[{j}].absentBecause", result.AbsentBecause);
        }

        static void Rules(string at, EquatableArray<IrNullReason> reasons)
        {
            if (reasons.Select((r, k) => (r, k)).FirstOrDefault(p => p.r.Rule == IrNullRule.NotInBoth) is ({ } reason, var k))
                throw new IrFormatException($"{at}[{k}].rule", $"is \"{IrWireNames.Of(reason.Rule)}\", {Why}.");
        }
    }

    private static IrObject Object(IrJsonObject json)
    {
        var results = json.Required("results");
        var kind = json.Name<IrObjectKind>("kind", IrWireNames.TryParse, "an object's kind");

        // A query runs its text and a procedure its name, so each has exactly what it runs.
        var text = json.OptionalString("text");
        if (kind == IrObjectKind.Query && text is null)
            throw new IrFormatException(json.PathOf("text"), "is missing: a query's object holds the text it runs.");
        if (kind != IrObjectKind.Query && text is not null)
            throw new IrFormatException(json.PathOf("text"), $"is given, but only a query runs text, and this object is a {IrWireNames.Of(kind)}.");
        var obj = new IrObject(
            json.String("id"),
            json.String("name"),
            kind,
            json.String("at"),
            [.. json.Array("parameters").Select(p => Parameter(IrJsonObject.At(p.Item, p.Path)))],

            // Null, not empty, when the results can't be listed: the writer's distinction, kept.
            results.ValueKind == JsonValueKind.Null
                ? null
                : [.. IrJsonObject.Items(results, json.PathOf("results")).Select(r => Result(IrJsonObject.At(r.Item, r.Path)))])
        {
            Text = text,
            ReturnsCode = json.Boolean("returnsCode"),
        };
        SetNames(obj, json);
        json.End();
        return obj;
    }

    /// <summary>
    /// An object's sets are named all or none, and no two alike, since each name is a key of the one object its function
    /// returns (<c>docs/decisions/0070-a-procedure-s-result-sets-are-named-all-or-none.md</c>).
    /// </summary>
    private static void SetNames(IrObject obj, IrJsonObject json)
    {
        if (obj.Results is not { } results || results.All(r => r.Name is null))
            return;
        if (results.Any(r => r.Name is null))
            throw new IrFormatException(json.PathOf("results"), "names some of its sets and not others; an object's sets are named all or none.");
        if (results.GroupBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new IrFormatException(json.PathOf("results"), $"names two sets \"{twice.Key}\"; each set's name is its own.");
    }

    private static IrParameter Parameter(IrJsonObject json)
    {
        var parameter = new IrParameter(json.String("name"), Type(json.Object("type")), json.Boolean("hasDefault"), json.Boolean("output"))
        {
            TableType = json.OptionalString("tableType"),
        };
        json.End();
        return parameter;
    }

    private static IrResult Result(IrJsonObject json)
    {
        var type = json.Object("type");
        var result = new IrResult(
            json.Name<IrEncoding>("encoding", IrWireNames.TryParse, "a result's encoding"),
            Type(type),
            Reasons(json, "mayBeAbsent", "absentBecause"))
        {
            Name = SetName(json),
            Cardinality = json.Name<IrCardinality>("cardinality", IrWireNames.TryParse, "a cardinality"),
        };
        json.End();
        if (result.Encoding == IrEncoding.Rowset && !IsRows(result.Type, union: true))
            throw new IrFormatException(type.Path, "is not what a rowset's type is: an array of rows, each a named type or an unknown, a union of such arrays, an unknown, or a type the config file declared.");
        return result;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is what a rowset's type is: an array of rows, each an object type named in <c>types</c> or an
    /// unknown; a union of such arrays when statements on different paths return different rows (<paramref name="union"/>); an
    /// unknown; or what the config file declared it to be, a type the application has (<see cref="IrImport"/>), alone or as the
    /// items of arrays. A driver hands a rowset to its caller as rows keyed by column, so no other node can describe one.
    /// </summary>
    private static bool IsRows(IrType type, bool union) => type switch
    {
        IrArray { Items: IrRef or IrUnknownType } or IrUnknownType => true,
        IrUnion { Variants: var variants } when union => variants.All(v => IsRows(v, union: false)),
        _ => Declared(type),
    };

    /// <summary>Whether <paramref name="type"/> is a type the config file declared, or arrays of one.</summary>
    private static bool Declared(IrType type) => type is IrImport || (type is IrArray { Items: var items } && Declared(items));

    /// <summary>
    /// A set's name, a key of its function's object, so an identifier and none of <see cref="IrResult.KeysBesideSets"/>; null when
    /// the set is unnamed.
    /// </summary>
    private static string? SetName(IrJsonObject json)
    {
        var name = json.OptionalString("name");
        if (name is not null && !IrIdentifiers.IsIdentifier(name))
            throw new IrFormatException(json.PathOf("name"), $"is \"{name}\", which is not an identifier.");
        if (name is not null && IrResult.KeysBesideSets.Contains(name, StringComparer.Ordinal))
            throw new IrFormatException(json.PathOf("name"), $"is \"{name}\", which can't name a set: the object its function returns already has that key.");
        return name;
    }

    private static Dictionary<string, IrObjectType> Types(IrJsonObject json)
    {
        // A name given twice is refused when the object is read (IrJsonObject), so each name here is one type.
        var types = new Dictionary<string, IrObjectType>(StringComparer.Ordinal);
        foreach (var name in json.Names.ToList())
            types.Add(name, ObjectType(json.Object(name)));
        json.End();
        return types;
    }

    private static EquatableMap<string, IrFold> Folds(IrJsonObject json)
    {
        var folds = json.Names.ToList().ToEquatableMap(name => name, name => Fold(json.Object(name)));
        json.End();
        return folds;
    }

    private static IrFold Fold(IrJsonObject json)
    {
        var ignored = json.String("ignored");
        var classes = json.Object("classes");
        var fold = new IrFold(ignored, classes.Names.ToList().ToEquatableMap(name => name, classes.NonEmptyString));
        classes.End();
        json.End();
        return fold;
    }

    private static IrObjectType ObjectType(IrJsonObject json)
    {
        var kind = json.String("kind");
        if (kind != "object")
            throw new IrFormatException(json.PathOf("kind"), $"is \"{kind}\", but a named type is always \"object\".");

        var type = new IrObjectType([.. json.Array("fields").Select(f => Field(IrJsonObject.At(f.Item, f.Path)))])
        {
            // Left out when false: a type that is not recursive says nothing.
            Recursive = json.OptionalBoolean("recursive") ?? false,
            NameDeclared = json.OptionalBoolean("nameDeclared") ?? false,
            Open = json.OptionalBoolean("open") ?? false,
            Identity = json.OptionalObject("identity") is { } identity ? Identity(identity) : null,
        };
        json.End();
        return type;
    }

    private static IrTypeIdentity Identity(IrJsonObject json)
    {
        // The owner's kind is the key, and its value the owner: { "procedure": "sales.get_order", "path": "results[0][]" }.
        var owners = json.Names.Where(n => n != "path").ToList();
        if (owners.Count != 1)
            throw new IrFormatException(json.Path, "must name one owner, keyed by its kind, beside its path.");
        if (!IrWireNames.TryParse(owners[0], out IrTypeOwner owner))
            throw new IrFormatException(json.PathOf(owners[0]), $"is not a kind of object a type belongs to in IR {IrDocument.CurrentVersion}.");

        var identity = new IrTypeIdentity(owner, json.String(owners[0]), json.String("path"));
        json.End();
        return identity;
    }

    private static IrField Field(IrJsonObject json)
    {
        var field = new IrField(
            json.String("name"),
            Type(json.Object("type")),
            json.Name<IrPresence>("presence", IrWireNames.TryParse, "a presence"),
            Reasons(json, "nullable", "nullableBecause"))
        {
            From = From(json),
            Sides = json.OptionalObject("sides") is { } sides ? Sides(sides) : null,
        };
        json.End();
        return field;
    }

    /// <summary>A field's source columns (0.25); none when the list is left out, and a list given is never empty.</summary>
    private static EquatableArray<IrSourceColumn> From(IrJsonObject json)
    {
        if (!json.Has("from"))
            return [];
        EquatableArray<IrSourceColumn> from = [.. json.Array("from").Select(c => SourceColumn(IrJsonObject.At(c.Item, c.Path)))];
        return from.Count > 0 ? from : throw new IrFormatException(json.PathOf("from"), "is empty: a field that reads no column leaves it out.");
    }

    private static IrSourceColumn SourceColumn(IrJsonObject json)
    {
        var column = new IrSourceColumn(json.String("column"), json.OptionalString("sql"), json.String("at"));
        json.End();
        return column;
    }

    /// <summary>Each database's field in a combined one (0.25): at least one of them gives it.</summary>
    private static IrFieldSides Sides(IrJsonObject json)
    {
        var sides = new IrFieldSides(
            json.OptionalObject("baseline") is { } baseline ? Side(baseline) : null,
            json.OptionalObject("candidate") is { } candidate ? Side(candidate) : null);
        json.End();
        return sides.Baseline is null && sides.Candidate is null
            ? throw new IrFormatException(json.Path, "names neither side: a combined field comes from at least one of the two databases.")
            : sides;
    }

    private static IrFieldSide Side(IrJsonObject json)
    {
        var side = new IrFieldSide(
            json.Name<IrPresence>("presence", IrWireNames.TryParse, "a presence"),
            Reasons(json, "nullable", "nullableBecause"),
            json.OptionalString("sql"))
        {
            From = From(json),
        };
        json.End();
        return side.Presence == IrPresence.Optional
            ? throw new IrFormatException(json.PathOf("presence"), "is \"optional\", which only a combined field is: each side is one database's.")
            : side;
    }

    /// <summary>
    /// The reasons a flag gives: the flag and its list agree, true with at least one reason, false with none and the list
    /// left out. A flag the writer leaves out when false (<paramref name="flagOptional"/>) reads as false.
    /// </summary>
    private static EquatableArray<IrNullReason> Reasons(IrJsonObject json, string flag, string list, bool flagOptional = false)
    {
        var set = flagOptional ? json.OptionalBoolean(flag) ?? false : json.Boolean(flag);
        if (!set)
        {
            return json.Has(list)
                ? throw new IrFormatException(json.PathOf(list), $"is given, but {flag} is false: a value that is never null has no reasons.")
                : [];
        }

        if (!json.Has(list))
            throw new IrFormatException(json.PathOf(list), $"is missing, but {flag} is true: every null needs a reason.");
        EquatableArray<IrNullReason> reasons = [.. json.Array(list).Select(r => Reason(IrJsonObject.At(r.Item, r.Path)))];
        return reasons.Count > 0 ? reasons : throw new IrFormatException(json.PathOf(list), $"is empty, but {flag} is true: every null needs a reason.");
    }

    private static IrNullReason Reason(IrJsonObject json)
    {
        var reason = new IrNullReason(json.Name<IrNullRule>("rule", IrWireNames.TryParse, "a null rule"), json.String("at")) { Detail = json.OptionalString("detail") };
        json.End();
        return reason;
    }

    private static IrType Type(IrJsonObject json)
    {
        // A reference is the one node without a kind.
        if (json.Has("ref"))
        {
            var reference = new IrRef(json.String("ref"));
            json.End();
            return reference;
        }

        var kind = json.String("kind");
        IrType type = kind switch
        {
            _ when IrWireNames.TryParse(kind, out IrScalarKind scalar) => Scalar(json, scalar),
            "array" => Array(json),
            "tuple" => new IrTuple([.. json.Array("items").Select(e => Element(IrJsonObject.At(e.Item, e.Path)))]),
            "record" => new IrRecord(Type(json.Object("values")), Reasons(json, "valuesNullable", "valuesNullableBecause")),
            "union" => Union(json),
            "json-text" => new IrJsonText(json.OptionalObject("content") is { } content ? Type(content) : null),
            "any-json" => new IrAnyJson(json.OptionalBoolean("objectOrArray") ?? false),
            "import" => new IrImport(json.String("name"), json.String("from")),
            "unknown" => new IrUnknownType(json.Name<IrUnknownReason>("reason", IrWireNames.TryParse, "an unknown's reason")),
            _ => throw new IrFormatException(json.PathOf("kind"), $"is \"{kind}\", which is not a type in IR {IrDocument.CurrentVersion}."),
        };
        json.End();
        return type;
    }

    private static IrUnion Union(IrJsonObject json)
    {
        EquatableArray<IrType> variants = [.. json.Array("variants").Select(v => Type(IrJsonObject.At(v.Item, v.Path)))];
        return variants.Count >= 2
            ? new IrUnion(variants)
            : throw new IrFormatException(json.PathOf("variants"), "must have at least two variants: a value of one type is that type, not a union.");
    }

    private static IrArray Array(IrJsonObject json) => new(Type(json.Object("items")))
    {
        // Left out when no item is ever null, as FOR JSON's arrays are.
        ItemsNullableBecause = Reasons(json, "itemsNullable", "itemsNullableBecause", flagOptional: true),
        MinItems = json.OptionalCount("minItems"),
        MaxItems = json.OptionalCount("maxItems"),
    };

    private static IrElement Element(IrJsonObject json)
    {
        var element = new IrElement(Type(json.Object("type")), Reasons(json, "nullable", "nullableBecause"));
        json.End();
        return element;
    }

    private static IrScalar Scalar(IrJsonObject json, IrScalarKind kind)
    {
        var scalar = new IrScalar(
            kind,
            json.OptionalName<IrFormat>("format", IrWireNames.TryParse, "a scalar's format"),
            json.OptionalString("sql"),
            Facets(json));
        if (kind == IrScalarKind.Bytes)
            Bytes(json, scalar);
        return scalar;
    }

    /// <summary>
    /// What a binary value may say (0.27): no format, and of the facets only a length, counted in bytes with no encoding, since its
    /// bytes are the value and not text. Why: docs/decisions/0081-binary-values-are-a-kind-of-their-own-counted-in-bytes.md.
    /// </summary>
    private static void Bytes(IrJsonObject json, IrScalar scalar)
    {
        const string Why = "which a binary value doesn't have: of the facets it has only a length, counted in bytes";
        if (scalar.Format is not null)
            throw new IrFormatException(json.PathOf("format"), "is given on a binary value (kind \"bytes\"), which has no format.");
        foreach (var facet in (string[])["minLength", "digits", "min", "max", "values"])
        {
            if (json.Has(facet))
                throw new IrFormatException(json.PathOf(facet), $"is given on a binary value (kind \"bytes\"), {Why}.");
        }

        if (scalar.Facets?.Length is { } length && (length.Unit != IrLengthUnit.Bytes || length.Encoding != IrByteEncoding.Unknown))
        {
            var at = json.PathOf(length.IsFixed ? "length" : "maxLength");
            throw new IrFormatException(at, $"of a binary value (kind \"bytes\") is counted in {IrWireNames.Of(length.Unit)}{(length.Encoding == IrByteEncoding.Unknown ? "" : " of an encoding")}: a binary value's length counts its bytes, which encode nothing.");
        }
    }

    /// <summary>The facets, which the writer puts beside the scalar's kind rather than in an object of their own.</summary>
    private static IrFacets Facets(IrJsonObject json)
    {
        if (json.Has("length") && json.Has("maxLength"))
            throw new IrFormatException(json.Path, "has both length and maxLength: a string's length is fixed or a maximum, not both.");
        var length = json.OptionalObject("length") is { } fixedLength
            ? Length(fixedLength, isFixed: true)
            : json.OptionalObject("maxLength") is { } maxLength ? Length(maxLength, isFixed: false) : null;
        return new IrFacets(
            length,
            json.OptionalObject("minLength") is { } minLength ? MinLength(minLength) : null,
            json.OptionalObject("digits") is { } digits ? Digits(digits) : null,
            json.OptionalObject("min") is { } min ? Bound(min) : null,
            json.OptionalObject("max") is { } max ? Bound(max) : null,
            json.OptionalObject("values") is { } values ? ValueSet(values) : null);
    }

    private static IrLength Length(IrJsonObject json, bool isFixed)
    {
        var length = new IrLength(
            json.Count("value"),
            isFixed,
            json.Name<IrLengthUnit>("unit", IrWireNames.TryParse, "a length's unit"),

            // Left out when not known.
            json.OptionalName<IrByteEncoding>("encoding", IrWireNames.TryParse, "a byte encoding") ?? IrByteEncoding.Unknown,
            Source(json.Object("from")));
        json.End();
        return length;
    }

    private static IrMinLength MinLength(IrJsonObject json)
    {
        // UTF-16 code units when it says no unit; the one unit it may say is code points (0.29), so that each minimum is written one way.
        var unit = json.OptionalName<IrLengthUnit>("unit", IrWireNames.TryParse, "a length's unit");
        if (unit is not (null or IrLengthUnit.CodePoints))
            throw new IrFormatException(json.PathOf("unit"), $"is \"{IrWireNames.Of(unit.Value)}\": a minimum length in UTF-16 code units says no unit, and the one it may say is \"code-points\".");
        var minLength = new IrMinLength(json.Count("value"), Source(json.Object("from"))) { Unit = unit ?? IrLengthUnit.Utf16 };
        json.End();
        return minLength;
    }

    private static IrDigits Digits(IrJsonObject json)
    {
        var digits = new IrDigits(json.Count("precision"), json.Count("scale"), Source(json.Object("from")));
        json.End();
        return digits;
    }

    private static IrBound Bound(IrJsonObject json)
    {
        var bound = new IrBound(Number(json.Required("value"), json.PathOf("value")), json.Boolean("inclusive"), Source(json.Object("from")));
        json.End();
        return bound;
    }

    private static IrValueSet ValueSet(IrJsonObject json)
    {
        var set = new IrValueSet(
            [.. json.Array("items").Select(i => i.Item.ValueKind == JsonValueKind.String ? new IrValue(IrJsonObject.AsString(i.Item, i.Path), IsString: true) : new IrValue(Number(i.Item, i.Path), IsString: false))],
            json.Name<IrValueMatch>("match", IrWireNames.TryParse, "a value set's match"),
            [.. json.Array("from").Select(f => Source(IrJsonObject.At(f.Item, f.Path)))],
            json.OptionalString("fold"));
        if (set.Match == IrValueMatch.Canonical ? set.Fold is null : set.Fold is not null)
            throw new IrFormatException(json.PathOf("fold"), "names the fold of a canonical value set, and is there exactly when the match is canonical.");
        json.End();
        return set;
    }

    /// <summary>A number as its exact decimal text, never through a double, in the plain form the writer writes.</summary>
    private static string Number(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number)
            throw new IrFormatException(path, "must be a number.");
        var text = element.GetRawText();
        return IrJsonWriter.IsPlainDecimal(text) ? text : throw new IrFormatException(path, $"is {text}, which is not a plain decimal number such as -12.5.");
    }

    /// <summary>
    /// The SHA-256 of each script file the locations point into (0.25); none when the file leaves <c>files</c> out. An empty object
    /// is refused: the writer leaves it out, so there is one way to say "none".
    /// </summary>
    private static EquatableMap<string, string> Files(IrJsonObject? json)
    {
        if (json is null)
            return [];
        var files = json.Names.ToList().ToEquatableMap(name => name, json.Sha256);
        json.End();
        return files.Count > 0 ? files : throw new IrFormatException(json.Path, "is empty: a source that points into no script leaves it out.");
    }

    /// <summary>The package a source was read from, or none when the file leaves <c>package</c> out; its hash comes with it.</summary>
    private static IrPackage? Package(IrJsonObject source)
    {
        if (source.Optional("package") is null)
            return source.Optional("sha256") is null ? null : throw new IrFormatException(source.PathOf("package"), "is missing, though sha256, its hash, is given.");
        return new IrPackage(source.String("package"), source.Sha256("sha256"));
    }

    /// <summary>
    /// The packages the source's package refers to; none when the file leaves <c>references</c> out. An empty array is refused:
    /// the writer leaves it out, so there is one way to say "none".
    /// </summary>
    private static EquatableArray<IrPackageReference> References(IrJsonObject source)
    {
        if (source.Optional("references") is null)
            return [];
        var references = source.Array("references").Select(r => Reference(IrJsonObject.At(r.Item, r.Path))).ToList();
        return references.Count > 0 ? [.. references] : throw new IrFormatException(source.PathOf("references"), "must list at least one package; leave references out when there are none.");
    }

    /// <summary>
    /// The packages the config file declared builds (0.31); none when the file leaves <c>builds</c> out. An empty array is refused:
    /// the writer leaves it out, so there is one way to say "none". Each is named once, and names a package the source read: its own
    /// <c>package</c>, or one of its <c>references</c> that was found, since a package not read can't be read as a build.
    /// </summary>
    private static EquatableArray<string> Builds(IrJsonObject json, IrSource source)
    {
        if (json.Optional("builds") is null)
            return [];
        var builds = new List<string>();
        foreach (var (item, path) in json.Array("builds"))
        {
            var name = IrJsonObject.AsString(item, path);
            if (builds.Contains(name, StringComparer.Ordinal))
                throw new IrFormatException(path, $"is \"{name}\", named twice: a package is declared a build once.");
            if (name != source.Package?.Name && !source.References.Any(r => r.Name == name && r.Sha256 is not null))
                throw new IrFormatException(path, $"is \"{name}\", which names no package the source read: a build is its package or a reference found.");
            builds.Add(name);
        }

        return builds.Count > 0 ? [.. builds] : throw new IrFormatException(json.PathOf("builds"), "must list at least one package; leave builds out when none is declared.");
    }

    /// <summary>
    /// The folders a source was read from (0.32); none when the file leaves <c>paths</c> out. Fewer than two are refused, since one
    /// folder is the source's <c>path</c>, and so is <c>path</c> beside them, a folder named twice, and an empty name: each is said
    /// one way only.
    /// </summary>
    private static EquatableArray<string> Paths(IrJsonObject json)
    {
        if (json.Optional("paths") is null)
            return [];
        if (json.Optional("path") is not null)
            throw new IrFormatException(json.PathOf("path"), "is given beside paths: a source read from several folders has no one path.");
        var folders = new List<string>();
        foreach (var (item, path) in json.Array("paths"))
        {
            var folder = IrJsonObject.AsString(item, path);
            if (folder.Length == 0)
                throw new IrFormatException(path, "is empty: a folder is named by its path.");
            if (folders.Contains(folder, StringComparer.Ordinal))
                throw new IrFormatException(path, $"is \"{folder}\", named twice: a folder is read once.");
            folders.Add(folder);
        }

        return folders.Count >= 2 ? [.. folders] : throw new IrFormatException(json.PathOf("paths"), "must list at least two folders; a source read from one folder names it in path.");
    }

    /// <summary>A package the source's package refers to: its name, its hash when it was found, and its database when another.</summary>
    private static IrPackageReference Reference(IrJsonObject json)
    {
        var reference = new IrPackageReference(
            json.String("package"),
            json.OptionalSha256("sha256"),
            json.OptionalString("database"));
        json.End();
        return reference;
    }

    /// <summary>
    /// The options the callers' sessions are declared to set, sorted by name; none when the file leaves <c>session</c> out. An
    /// empty object is refused: the writer leaves it out, so there is one way to say "none".
    /// </summary>
    private static EquatableArray<IrSessionOption> Session(IrJsonObject? json)
    {
        if (json is null)
            return [];
        var options = json.Names.Select(name => new IrSessionOption(name, json.Boolean(name))).OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
        if (options.Count == 0)
            throw new IrFormatException(json.Path, "must declare at least one option; leave session out when none is declared.");
        json.End();
        return [.. options];
    }

    /// <summary>Where a fact came from: one property, whose name is the kind of source and whose value is its ref.</summary>
    private static IrFactSource Source(IrJsonObject json)
    {
        var names = json.Names.ToList();
        if (names.Count != 1)
            throw new IrFormatException(json.Path, "must have one property, whose name says where the fact came from.");
        if (!IrWireNames.TryParse(names[0], out IrFactSourceKind kind))
            throw new IrFormatException(json.PathOf(names[0]), $"is not where a fact can come from in IR {IrDocument.CurrentVersion}.");
        var source = new IrFactSource(kind, json.String(names[0]));
        json.End();
        return source;
    }

    private static IrUnknown Unknown(IrJsonObject json)
    {
        var unknown = new IrUnknown(
            json.String("object"),
            json.String("path"),
            json.Name<IrUnknownReason>("reason", IrWireNames.TryParse, "an unknown's reason"),
            json.String("detail"),
            json.String("at"));
        json.End();
        return unknown;
    }

    private static IrOverride Override(IrJsonObject json)
    {
        var claim = new IrOverride(
            json.String("object"),
            json.String("path"),
            json.Name<IrOverrideSetting>("setting", IrWireNames.TryParse, "an override's setting"),
            json.OptionalString("why"))
        {
            Via = json.OptionalString("via"),
        };
        json.End();
        return claim;
    }
}
