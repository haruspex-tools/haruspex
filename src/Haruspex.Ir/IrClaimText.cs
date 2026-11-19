using System.Globalization;

namespace Haruspex.Ir;

/// <summary>
/// Claims in words, for the <see cref="IrChange.Before"/> and <see cref="IrChange.After"/> of a change: a type as
/// <c>number (int32)</c>, a length as <c>at most 50</c>, a value set's items as <c>"open", "held"</c>. Short, and in the IR's own
/// vocabulary (<see cref="IrWireNames"/>), so a change reads the same as the file it came from.
/// </summary>
public static class IrClaimText
{
    /// <summary>The most items of a value set listed; a set past it is cut short, saying how many more there are.</summary>
    private const int ItemsListed = 10;

    /// <summary>A type node, without what lies behind a reference: <c>string</c>, <c>number (int32)</c>, <c>an array of the object OrderRow</c>.</summary>
    public static string Type(IrType type) => type switch
    {
        IrScalar scalar => scalar.Format is { } format ? $"{IrWireNames.Of(scalar.Kind)} ({IrWireNames.Of(format)})" : IrWireNames.Of(scalar.Kind),
        IrArray array => $"an array of {Type(array.Items)}",
        IrTuple tuple => $"a tuple of {Count(tuple.Items.Count)}",
        IrRecord record => $"a record of {Type(record.Values)}",
        IrUnion union => $"one of {string.Join(", ", union.Variants.Select(Type))}",
        IrRef reference => $"the object {reference.Name}",
        IrJsonText text => text.Content is { } content ? $"JSON text of {Type(content)}" : "JSON text",
        IrImport import => $"{import.Name} from {import.From}",
        IrUnknownType unknown => $"unknown ({IrWireNames.Of(unknown.Reason)})",
    };

    /// <summary>A value as a field or a parameter holds it: its type, its SQL type when it has one, and whether it may be null.</summary>
    public static string Value(IrType type, bool nullable)
    {
        var sql = type is IrScalar { Sql: { } written } ? ", " + written : "";
        return Type(type) + sql + (nullable ? ", nullable" : "");
    }

    /// <summary>
    /// A length: <c>at most 50</c>, <c>exactly 10</c>, with the unit when it is bytes and their encoding when it is known
    /// (<c>at most 20 bytes (utf8)</c>); <c>none</c> when there is none.
    /// </summary>
    public static string Length(IrLength? length) => length is null
        ? "none"
        : (length.IsFixed ? "exactly " : "at most ") + Count(length.Value) + Unit(length);

    private static string Unit(IrLength length) => (length.Unit, length.Encoding) switch
    {
        (IrLengthUnit.Utf16, _) => "",
        (IrLengthUnit.Bytes, IrByteEncoding.Unknown) => " bytes",
        (IrLengthUnit.Bytes, var encoding) => $" bytes ({IrWireNames.Of(encoding)})",
    };

    /// <summary>A minimum length: <c>at least 1</c>, or <c>none</c>.</summary>
    public static string MinLength(IrMinLength? length) => length is null ? "none" : "at least " + Count(length.Value);

    /// <summary>A decimal's digits: <c>precision 10, scale 2</c>, or <c>none</c>.</summary>
    public static string Digits(IrDigits? digits) => digits is null ? "none" : $"precision {Count(digits.Precision)}, scale {Count(digits.Scale)}";

    /// <summary>A lower bound: <c>at least 0</c> or <c>more than 0</c>, or <c>none</c>.</summary>
    public static string Min(IrBound? bound) => bound is null ? "none" : (bound.Inclusive ? "at least " : "more than ") + bound.Value;

    /// <summary>An upper bound: <c>at most 100</c> or <c>less than 100</c>, or <c>none</c>.</summary>
    public static string Max(IrBound? bound) => bound is null ? "none" : (bound.Inclusive ? "at most " : "less than ") + bound.Value;

    /// <summary>How many items an array has: <c>at least 1</c>, <c>at most 3</c>, <c>1 to 3</c>, or <c>any</c>.</summary>
    public static string Items(int? min, int? max) => (min, max) switch
    {
        (null, null) => "any",
        ({ } least, null) => "at least " + Count(least),
        (null, { } most) => "at most " + Count(most),
        ({ } least, { } most) when least == most => "exactly " + Count(least),
        ({ } least, { } most) => $"{Count(least)} to {Count(most)}",
    };

    /// <summary>A value set's items, in the IR's order: a string as JSON (<c>"open"</c>), a number as its exact text, cut short past ten.</summary>
    public static string Items(IEnumerable<IrValue> items)
    {
        var all = items.ToList();
        var listed = string.Join(", ", all.Take(ItemsListed).Select(v => v.IsString ? IrPath.Quote(v.Text) : v.Text));
        return all.Count > ItemsListed ? $"{listed} and {Count(all.Count - ItemsListed)} more" : listed;
    }

    /// <summary>The options the callers' sessions are declared to set: <c>ansiWarnings on</c>, or <c>none declared</c>.</summary>
    public static string Session(IEnumerable<IrSessionOption> options)
    {
        var declared = options.Select(o => $"{o.Name} {(o.On ? "on" : "off")}").ToList();
        return declared.Count == 0 ? "none declared" : string.Join(", ", declared);
    }

    /// <summary>A fold: how many characters it ignores and how many classes it has, or <c>none</c>.</summary>
    public static string Fold(IrFold? fold) => fold is null
        ? "none"
        : $"a fold ignoring {Count(fold.Ignored.Length)} characters, with {Count(fold.Classes.Count)} classes";

    /// <summary>How many result sets: <c>1 result set</c>, <c>2 result sets</c>.</summary>
    public static string ResultSets(int count) => $"{Count(count)} result set{(count == 1 ? "" : "s")}";

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);
}
