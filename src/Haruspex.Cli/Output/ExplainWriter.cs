using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Cli.Output;

/// <summary>
/// What <c>haruspex explain</c> prints: text for a person, or (<c>--json</c>) one JSON document for tools, an editor's extension
/// or a review bot. The JSON's shape is in docs/command-line.md, carries its own <c>version</c> and is provisional until 1.0; it
/// uses the IR's vocabulary, its codes and its path syntax, so a tool that reads the IR reads it too. Written with
/// <see cref="Utf8JsonWriter"/>, which needs no reflection, so Native AOT compiles it as it is.
/// </summary>
/// <param name="document">The IR explained.</param>
/// <param name="sources">The SQL each reference points at, when it is at hand.</param>
/// <param name="json">Whether to write JSON rather than text.</param>
internal sealed class ExplainWriter(IrDocument document, ExplainSources sources, bool json)
{
    /// <summary>The JSON's version, which a tool checks; provisional until 1.0.</summary>
    public const int Version = 1;

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>One field in full: its value, the columns it is read from, each reason with where it applied, and what changes it.</summary>
    public void Field(string target, IrFieldPlace place, TextWriter output)
    {
        if (json)
        {
            Json(output, w =>
            {
                w.WriteString("object", target);
                w.WriteStartArray("fields");
                FieldJson(w, place, full: true);
                w.WriteEndArray();
            });
            return;
        }

        var field = place.Field;
        Say(output, $"{target} {place.Path}: {IrClaimText.Value(field.Type, field.Nullable)}{Presence(field)}");
        Steps(output, field.From, field.NullableBecause, baseline: false, "  ");
        if (IrSurprises.TypeDiffers(field) && field.Type is IrScalar { Sql: { } own })
            Say(output, $"  type {own}: what it is computed with makes it another type than its column's");
        if (field.Type is IrUnknownType unknown)
        {
            Say(output, $"  unknown {IrWireNames.Of(unknown.Reason)}: {IrReasons.Text(unknown.Reason)}");
            foreach (var found in Unknowns(place))
            {
                Say(output, $"    at {found.At}: {found.Detail}");
                Shown(output, found.At, baseline: false, "    ");
            }
        }

        if (field.Presence == IrPresence.Optional)
            Say(output, "  absent: one of the two databases doesn't return the key, or leaves it out when null where the other sends null");
        else if (IrSurprises.AbsentWhenNull(field))
            Say(output, "  absent when null: FOR JSON leaves out a key whose value is null");

        if (Override(place) is { } claim)
            Say(output, $"  declared {IrWireNames.Of(claim.Setting)} in the config file{(claim.Why is { } why ? $": {why}" : "")}");
        if (field.Sides is { } sides)
        {
            Side(output, sides.Baseline, baseline: true);
            Side(output, sides.Candidate, baseline: false);
            Say(output, $"  combined: {Combined(field)}");
        }
        else if (document.Source.Baseline is not null)
        {
            Say(output, $"  both databases ({sources.SourceOf(baseline: true)} and {sources.SourceOf(baseline: false)}) give it exactly as it is");
        }

        SayIfNotDerived(output);

        foreach (var way in WaysOut(field))
            Say(output, $"  to change it: {way}");
        foreach (var code in Codes(field))
            Say(output, $"  page {code}: {IrReasons.Page(code)}");
    }

    /// <summary>
    /// A derivation's steps: each column the value is read from, then each rule with where it applied, the line of SQL under each
    /// when it is at hand. A <c>nullable-column</c> reason names its column, which is shown where the column is declared.
    /// </summary>
    private void Steps(TextWriter output, EquatableArray<IrSourceColumn> from, EquatableArray<IrNullReason> because, bool baseline, string indent)
    {
        foreach (var column in from)
        {
            Say(output, $"{indent}from {column.Column}{(column.Sql is { } sql ? $" ({sql})" : "")}, declared at {column.At}");
            Shown(output, column.At, baseline, indent + "  ");
        }

        foreach (var reason in because)
        {
            Say(output, $"{indent}null {IrWireNames.Of(reason.Rule)} at {reason.At}: {IrReasons.Text(reason.Rule)}");
            Shown(output, Located(reason, from), baseline, indent + "  ");
        }
    }

    /// <summary>The line a reference points at, under it, when it is at hand; nothing otherwise.</summary>
    private void Shown(TextWriter output, string at, bool baseline, string indent)
    {
        if (sources.Line(at, baseline) is { } line)
            Say(output, $"{indent}| {line.Trim().Replace('\t', ' ')}");
    }

    /// <summary>Where a reason applied: its own location, or for a column it names, where that column is declared.</summary>
    private static string Located(IrNullReason reason, EquatableArray<IrSourceColumn> from) =>
        from.FirstOrDefault(c => c.Column == reason.At)?.At ?? reason.At;

    /// <summary>One database's side of a combined field, labelled with its source, and its own derivation.</summary>
    private void Side(TextWriter output, IrFieldSide? side, bool baseline)
    {
        var name = baseline ? "baseline" : "candidate";
        var source = sources.SourceOf(baseline);
        if (side is null)
        {
            Say(output, $"  {name} ({source}): doesn't return the key");
            return;
        }

        Say(output, $"  {name} ({source}): {side.Sql ?? "not a scalar"}, {IrWireNames.Of(side.Presence)}, {(side.NullableBecause.Count == 0 ? "never null" : "may be null")}");
        if (baseline && BaselineSaysNoSources)
            Say(output, "    the baseline was read from an IR older than 0.25, which doesn't say where its fields came from");
        Steps(output, side.From, side.NullableBecause, baseline, "    ");
    }

    /// <summary>
    /// Whether the baseline says nothing of where any field came from while the candidate does: it was given as an IR file written
    /// before 0.25, which has no derivations to show.
    /// </summary>
    private bool BaselineSaysNoSources
    {
        get
        {
            var sides = document.Types.Values.SelectMany(t => t.Fields).Select(f => f.Sides).OfType<IrFieldSides>().ToList();
            return sides.Any(s => s.Candidate is { From.Count: > 0 }) && !sides.Any(s => s.Baseline is { From.Count: > 0 });
        }
    }

    /// <summary>
    /// Whether the IR says where its fields came from: false for one written before 0.25, which has the reasons but neither the
    /// columns nor the files' hashes.
    /// </summary>
    private bool Derived => document.Version == IrDocument.CurrentVersion;

    /// <summary>For an IR written before 0.25, why no column is shown and no line, and what writes one that has them.</summary>
    private void SayIfNotDerived(TextWriter output)
    {
        if (!Derived)
            Say(output, $"  this IR is {document.Version}, which doesn't say where a field came from: run haruspex generate to write it as {IrDocument.CurrentVersion}");
    }

    /// <summary>The unknowns at the field's place, with their detail and where.</summary>
    private IEnumerable<IrUnknown> Unknowns(IrFieldPlace place)
    {
        var type = document.Types[place.TypeName];
        if (type.Identity is not { } identity)
            return [];
        var path = identity.Path + IrPath.Key(place.Field.Name);
        return document.Unknowns.Where(u => u.Object == identity.Object && u.Path == path);
    }

    /// <summary>A field of a type that belongs to no one object, named by the type; every field that is a surprise when none is named.</summary>
    public void Type(string name, IrField? field, TextWriter output)
    {
        var places = document.Types[name].Fields.Select(f => new IrFieldPlace(name, f, IrPath.Render([new IrPathKey(f.Name)]))).ToList();
        if (field is not null)
            Field(name, places.First(p => p.Field == field), output);
        else
            Surprises(name, places, [], output);
    }

    /// <summary>Every field of an object that is a surprise, a line each, and its unknowns.</summary>
    public void Object(IrObject obj, TextWriter output) =>
        Surprises(obj.Id, IrFieldPlaces.Fields(document, obj), [.. document.Unknowns.Where(u => u.Object == obj.Id)], output);

    /// <summary>A parameter: its declared type, and that a caller may pass it as null.</summary>
    public void Parameter(IrObject obj, IrParameter parameter, TextWriter output)
    {
        if (json)
        {
            Json(output, w =>
            {
                w.WriteString("object", obj.Id);
                w.WriteStartObject("parameter");
                w.WriteString("name", parameter.Name);
                w.WriteString("value", IrClaimText.Value(parameter.Type, nullable: true));
                if (parameter.Type is IrScalar { Sql: { } sql })
                    w.WriteString("sql", sql);
                w.WriteBoolean("hasDefault", parameter.HasDefault);
                w.WriteBoolean("output", parameter.IsOutput);
                w.WriteEndObject();
            });
            return;
        }

        Say(output, $"{obj.Id} {parameter.Name}: {IrClaimText.Value(parameter.Type, nullable: true)}");
        Say(output, $"  a parameter of {obj.Id}, which is defined at {obj.At}; a caller may pass it as null{(parameter.HasDefault ? ", or leave it out for its default" : "")}");
        if (parameter.IsOutput)
            Say(output, "  an OUTPUT parameter: the procedure may set it, and its function returns the value it ends with, which may be null");
        if (parameter.Type is IrUnknownType unknown)
            Say(output, $"  unknown {IrWireNames.Of(unknown.Reason)}: {IrReasons.Text(unknown.Reason)}");
    }

    private void Surprises(string target, IReadOnlyList<IrFieldPlace> places, IReadOnlyList<IrUnknown> unknowns, TextWriter output)
    {
        var surprises = places.Where(p => IrSurprises.Of(p.Field)).ToList();
        if (json)
        {
            Json(output, w =>
            {
                w.WriteString("object", target);
                w.WriteStartArray("fields");
                foreach (var place in surprises)
                    FieldJson(w, place, full: false);
                w.WriteEndArray();
                w.WriteStartArray("unknowns");
                foreach (var unknown in unknowns)
                {
                    w.WriteStartObject();
                    w.WriteString("path", unknown.Path);
                    Code(w, "reason", IrWireNames.Of(unknown.Reason), IrReasons.Text(unknown.Reason));
                    w.WriteString("detail", unknown.Detail);
                    w.WriteString("at", unknown.At);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            });
            return;
        }

        if (surprises.Count == 0 && unknowns.Count == 0)
        {
            Say(output, $"{target}: every field is what its columns declare.");
            SayIfNotDerived(output);
            return;
        }

        Say(output, surprises.Count switch
        {
            0 => $"{target}: no field differs from what its columns declare.",
            1 => $"{target}: 1 field differs from what its columns declare.",
            var n => $"{target}: {n} fields differ from what their columns declare.",
        });
        var width = surprises.Count == 0 ? 0 : surprises.Max(p => p.Path.Length);
        foreach (var place in surprises)
            Say(output, $"  {place.Path.PadRight(width)}  {Summary(place.Field)}");
        foreach (var unknown in unknowns)
            Say(output, $"  unknown {unknown.Path} ({IrWireNames.Of(unknown.Reason)}) at {unknown.At}: {unknown.Detail}");
        if (surprises.Count > 0)
            Say(output, $"Run haruspex explain {IrFieldPlaces.ShellWord(target)} <field> for a field in full.");
        SayIfNotDerived(output);
    }

    /// <summary>
    /// A line of text, with every control character in it written as an escape (<see cref="Visible"/>): a SQL line, a name or an
    /// unknown's detail may hold one, and printed raw an escape character would drive the terminal.
    /// </summary>
    private static void Say(TextWriter output, string line) => output.WriteLine(Visible.Of(line));

    /// <summary>A field's surprises on one line: <c>may be null: unmatched-row, empty-aggregate</c>.</summary>
    private static string Summary(IrField field)
    {
        var parts = new List<string>();
        if (field.Type is IrUnknownType unknown)
            parts.Add("unknown: " + IrWireNames.Of(unknown.Reason));
        if (IrSurprises.TypeDiffers(field) && field.Type is IrScalar { Sql: { } sql })
            parts.Add($"{sql}, not its column's type");
        if (IrSurprises.Reasons(field) is { Count: > 0 } reasons)
            parts.Add("may be null: " + string.Join(", ", reasons.Select(r => IrWireNames.Of(r.Rule)).Distinct()));
        if (field.Presence == IrPresence.Optional)
            parts.Add("may be left out");
        else if (IrSurprises.AbsentWhenNull(field))
            parts.Add("left out when null");
        return string.Join("; ", parts);
    }

    private void FieldJson(Utf8JsonWriter w, IrFieldPlace place, bool full)
    {
        var field = place.Field;
        w.WriteStartObject();
        w.WriteString("path", place.Path);
        w.WriteString("type", place.TypeName);
        w.WriteString("value", IrClaimText.Value(field.Type, field.Nullable));
        if (field.Type is IrScalar { Sql: { } sql })
            w.WriteString("sql", sql);
        w.WriteBoolean("nullable", field.Nullable);
        w.WriteString("presence", IrWireNames.Of(field.Presence));
        w.WriteBoolean("surprise", IrSurprises.Of(field));
        w.WriteBoolean("typeDiffers", IrSurprises.TypeDiffers(field));
        From(w, "from", field.From, baseline: false);
        Reasons(w, "nullableBecause", field.NullableBecause, field.From, baseline: false);
        if (field.Type is IrUnknownType unknown)
            Code(w, "unknown", IrWireNames.Of(unknown.Reason), IrReasons.Text(unknown.Reason));
        if (full)
        {
            if (Override(place) is { } claim)
            {
                w.WriteStartObject("override");
                w.WriteString("setting", IrWireNames.Of(claim.Setting));
                if (claim.Why is { } why)
                    w.WriteString("why", why);
                w.WriteEndObject();
            }

            if (field.Sides is { } sides)
            {
                w.WriteStartObject("sides");
                SideJson(w, "baseline", sides.Baseline, baseline: true);
                SideJson(w, "candidate", sides.Candidate, baseline: false);
                w.WriteEndObject();
            }
            else if (document.Source.Baseline is not null)
            {
                w.WriteBoolean("sameInBoth", true);
            }

            w.WriteStartArray("waysOut");
            foreach (var way in WaysOut(field))
                w.WriteStringValue(way);
            w.WriteEndArray();
        }

        w.WriteString("explain", IrFieldPlaces.Command(document, place.TypeName, field));
        w.WriteEndObject();
    }

    private void SideJson(Utf8JsonWriter w, string name, IrFieldSide? side, bool baseline)
    {
        if (side is null)
        {
            w.WriteNull(name);
            return;
        }

        w.WriteStartObject(name);
        w.WriteString("source", sources.SourceOf(baseline));
        if (side.Sql is { } sql)
            w.WriteString("sql", sql);
        w.WriteString("presence", IrWireNames.Of(side.Presence));
        w.WriteBoolean("nullable", side.NullableBecause.Count > 0);
        if (baseline)
            w.WriteBoolean("derived", !BaselineSaysNoSources);
        From(w, "from", side.From, baseline);
        Reasons(w, "nullableBecause", side.NullableBecause, side.From, baseline);
        w.WriteEndObject();
    }

    private void From(Utf8JsonWriter w, string name, IEnumerable<IrSourceColumn> from, bool baseline)
    {
        w.WriteStartArray(name);
        foreach (var column in from)
        {
            w.WriteStartObject();
            w.WriteString("column", column.Column);
            if (column.Sql is { } sql)
                w.WriteString("sql", sql);
            w.WriteString("at", column.At);
            Line(w, column.At, baseline);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private void Reasons(Utf8JsonWriter w, string name, IEnumerable<IrNullReason> reasons, EquatableArray<IrSourceColumn> from, bool baseline)
    {
        w.WriteStartArray(name);
        foreach (var reason in reasons)
        {
            w.WriteStartObject();
            Code(w, "rule", IrWireNames.Of(reason.Rule), IrReasons.Text(reason.Rule), inline: true);
            w.WriteString("at", reason.At);
            Line(w, Located(reason, from), baseline);
            w.WriteBoolean("declared", IrSurprises.IsDeclared(reason.Rule));
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    /// <summary>The line of SQL a reference points at, as <c>line</c>, when it is at hand; left out otherwise.</summary>
    private void Line(Utf8JsonWriter w, string at, bool baseline)
    {
        if (sources.Line(at, baseline) is { } line)
            w.WriteString("line", line.Trim());
    }

    /// <summary>A code, with its text and its page: as an object of its own, or <paramref name="inline"/> in the one being written.</summary>
    private static void Code(Utf8JsonWriter w, string name, string code, string text, bool inline = false)
    {
        if (!inline)
            w.WriteStartObject(name);
        w.WriteString(inline ? name : "code", code);
        w.WriteString("text", text);
        w.WriteString("page", IrReasons.Page(code));
        if (!inline)
            w.WriteEndObject();
    }

    private void Json(TextWriter output, Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, Options))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteBoolean("derived", Derived);
            body(w);
            w.WriteEndObject();
        }

        output.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        output.Write('\n');
    }

    private IrOverride? Override(IrFieldPlace place)
    {
        var type = document.Types[place.TypeName];
        if (type.Identity is not { } identity)
            return null;
        var path = identity.Path + IrPath.Key(place.Field.Name);
        return document.Overrides.FirstOrDefault(o => o.Object == identity.Object && o.Path == path);
    }

    private static string Presence(IrField field) => field.Presence switch
    {
        IrPresence.OmittedWhenNull when field.Nullable => ", left out when null",
        IrPresence.Optional => ", may be left out",
        _ => "",
    };

    /// <summary>How a combined field was made from the two: as wide as both, so the application accepts what either returns.</summary>
    private static string Combined(IrField field) =>
        $"{field.Type switch { IrScalar { Sql: { } sql } => sql, var other => IrClaimText.Type(other) }}, {IrWireNames.Of(field.Presence)}, "
        + (field.Nullable ? "null where either may be" : "never null in either")
        + ": as wide as both, so the application accepts what either database returns";

    private static IEnumerable<string> WaysOut(IrField field) =>
        field.NullableBecause.Select(r => IrReasons.WayOut(r.Rule))
            .Concat(field.Type is IrUnknownType unknown ? [IrReasons.WayOut(unknown.Reason)] : [])
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> Codes(IrField field) =>
        field.NullableBecause.Select(r => IrWireNames.Of(r.Rule))
            .Concat(field.Type is IrUnknownType unknown ? [IrWireNames.Of(unknown.Reason)] : [])
            .Distinct(StringComparer.Ordinal);
}
