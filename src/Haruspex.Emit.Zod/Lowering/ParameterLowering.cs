using System.Globalization;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Emit.Zod.Lowering;

/// <summary>
/// A procedure's parameters as the schema of what a caller may send: a strict object, one property per parameter, each value
/// checked against its declared type's facts. Results are read as they arrive; parameters are the other way round, a rule for
/// what may be sent, so this is apart from <see cref="TypeLowering"/>, sharing its numbers' checks (<see cref="Facets"/>).
/// </summary>
/// <remarks>
/// <para>
/// Why check at all: SQL Server converts what a caller sends to the parameter's type, cutting a longer string short and
/// rounding a decimal of more places, without an error, so for those the client's check is the only one there is
/// (<c>docs/decisions/0054-a-parameter-is-checked-against-its-declared-type.md</c>). The object is strict, so a misspelt name is
/// an error rather than a parameter silently left at its default (<c>docs/decisions/0009-parameters-are-strict.md</c>).
/// </para>
/// <para>
/// A property's key is the parameter's name without its <c>@</c>, as declared: a result's keys are its columns' names as SQL
/// Server sends them, and the parameters' keys follow the same rule. Renaming them belongs to naming rules for keys, which the
/// config file doesn't have yet.
/// </para>
/// </remarks>
internal static class ParameterLowering
{
    /// <summary>The declaration of <paramref name="obj"/>'s parameters' schema, or null when it has none.</summary>
    public static ZodDeclaration? Declaration(IrObject obj, TypeLowering types)
    {
        if (obj.Parameters.Count == 0)
            return null;
        var properties = obj.Parameters.Select(p => new ZodProperty(Key(p), Property(p, types)));
        return new ZodDeclaration(ZodModule.ParametersOf(obj.Name), $"{obj.Id}: its parameters, checked before anything is sent.", new ObjectSchema([.. properties], Strict: true));
    }

    /// <summary>
    /// The declaration of what <paramref name="obj"/>'s <c>OUTPUT</c> parameters hold when it ends, or null when it has none: each
    /// as the mssql driver hands it over, which is as it hands a column of a row over (a <c>bigint</c> as a string, a date as a
    /// <c>Date</c>; observed), and NULL whatever the procedure does, since nothing proves it assigns one. One with a default may
    /// be left unbound by the caller, and then it doesn't come back.
    /// </summary>
    public static ZodDeclaration? OutputDeclaration(IrObject obj, TypeLowering types)
    {
        var outputs = obj.Parameters.Where(p => p.IsOutput).ToList();
        if (outputs.Count == 0)
            return null;
        var properties = outputs.Select(p =>
        {
            var value = types.Type(p.Type, IrEncoding.Rowset);
            value = p.Type is IrUnknownType ? value : value.Then("nullable");
            return new ZodProperty(Key(p), p.HasDefault ? value.Then("optional") : value);
        });
        return new ZodDeclaration(ZodModule.OutputOf(obj.Name), $"{obj.Id}: what its OUTPUT parameters hold when it ends.", new ObjectSchema([.. properties], Strict: false));
    }

    /// <summary>The parameter's key: its name without the <c>@</c> that every T-SQL parameter's name starts with.</summary>
    public static string Key(IrParameter parameter) => parameter.Name.StartsWith('@') ? parameter.Name[1..] : parameter.Name;

    /// <summary>
    /// A parameter's property: its value, which may be null, and may be left out when the parameter has a default or is an
    /// <c>OUTPUT</c> one (the procedure is then sent NULL for it, as <c>EXEC</c> with an unset variable would send).
    /// </summary>
    /// <remarks>
    /// Every parameter accepts NULL: an interpreted procedure can't declare one <c>NOT NULL</c>, so NULL is a value it may be
    /// sent. A guard that ends the call when a parameter is NULL proves NULL is never useful; when the IR records that, this is
    /// the one place the schema follows it. <c>z.unknown()</c> accepts NULL already.
    /// </remarks>
    private static ZodExpression Property(IrParameter parameter, TypeLowering types)
    {
        var value = Value(parameter.Type, types);
        if (parameter.Type is not IrUnknownType)
            value = value.Then("nullable");
        if (parameter.HasDefault || parameter.IsOutput)
            value = value.Then("optional");
        return parameter.IsOutput
            ? new Noted(value, new ZodNote("An OUTPUT parameter: it may be left out, and what it holds when the procedure ends comes back with the results."))
            : value;
    }

    private static ZodExpression Value(IrType type, TypeLowering types) => type switch
    {
        IrScalar { Kind: IrScalarKind.String } scalar => Text(scalar),
        IrScalar { Kind: IrScalarKind.Number, Format: IrFormat.Int64 } => BigInt(),
        IrScalar { Kind: IrScalarKind.Number } scalar => Number(scalar),
        IrScalar { Kind: IrScalarKind.Date or IrScalarKind.Time or IrScalarKind.Datetime } scalar => DateTimes.Of(scalar),

        // A boolean and a GUID are checked as a result in JSON is.
        IrScalar scalar => types.Type(scalar, IrEncoding.Json),
        IrJsonText => new Noted(Z("string"), new ZodNote("JSON text, sent as it is: what it should hold isn't typed yet.")),

        // A table-valued parameter, or a type not modelled: z.unknown(), with the IR's reason.
        _ => types.Type(type, IrEncoding.Json),
    };

    /// <summary>
    /// A <c>bigint</c>: a safe integer, a JavaScript <c>bigint</c>, or a string of its digits, which is how a rowset result hands
    /// one over, so a value read can be sent back. A number past 2^53 is rounded before the driver sees it, so it can't be
    /// one; the driver sends a <c>bigint</c> or a string exactly.
    /// </summary>
    private static ZodExpression BigInt()
    {
        var least = long.MinValue.ToString(CultureInfo.InvariantCulture);
        var greatest = long.MaxValue.ToString(CultureInfo.InvariantCulture);
        return Z("union", new ArrayLiteral([
            Z("int"),
            Z("bigint").Then("min", new BigIntLiteral(least)).Then("max", new BigIntLiteral(greatest)),
            Z("string").Then("regex", new Pattern(@"-?\d{1,19}"))
                .Then("refine", new IntegerTextRange(least, greatest), new ObjectLiteral([new ZodProperty("error", new Text("Outside the range of bigint"))])),
        ]));
    }

    /// <summary>
    /// A string, at most its length: a longer one is cut short. A fixed length is a maximum too, since a shorter value is padded.
    /// A length in UTF-16 code units is exact, as JavaScript's <c>length</c> counts them; one in bytes is checked in code units, of
    /// which a character takes no more than it takes bytes, so the check is necessary but, outside a single-byte code page, not
    /// enough, and the note says what more the server may change.
    /// </summary>
    /// <remarks>
    /// Not <c>.max(n)</c>: Zod 4.6.7 counts a string's length in code points, so a character outside the Basic Multilingual Plane
    /// counts once, where SQL Server counts two code units, and a value of <c>n</c> code points but more code units would pass
    /// and be cut short (<c>ParameterAcceptanceTests</c>).
    /// </remarks>
    private static ZodExpression Text(IrScalar scalar)
    {
        if (scalar.Facets?.Length is not { } length)
            return Z("string");
        var count = length.Value.ToString(CultureInfo.InvariantCulture);
        var code = Z("string").Then("refine", new UnitsAtMost(length.Value), new ObjectLiteral([new ZodProperty("error", new Text($"More than {count} UTF-16 code units, which {scalar.Sql ?? "the string"} would cut short"))]));
        if (length.Unit == IrLengthUnit.Utf16)
            return code;

        var sql = scalar.Sql ?? "The string";
        var n = count;
        var note = length.Encoding switch
        {
            IrByteEncoding.SingleByte => $"{sql} holds only its collation's code page, and replaces any other character without an error.",
            IrByteEncoding.Utf8 => $"{sql} counts bytes of UTF-8, up to four a character, so a value of fewer than {n} characters may still be cut short.",
            IrByteEncoding.DoubleByte => $"{sql} counts bytes of a double-byte code page, so a value of fewer than {n} characters may still be cut short, and a character the code page lacks is replaced.",
            _ => $"{sql} counts bytes and its collation isn't declared, so a value of fewer than {n} characters may still be cut short, and a character its code page lacks is replaced.",
        };
        return new Noted(code, new ZodNote(note));
    }

    /// <summary>
    /// A number, held to its type's range and places as a number in JSON is (<see cref="Facets.Number"/>): a value of more places
    /// is rounded and an integer's fraction dropped, without an error. A decimal of more digits than a double keeps can't be
    /// sent exactly as a JavaScript number, and a floating-point type rounds to the nearest value it holds (<c>real</c> to about
    /// seven digits), which the notes say: rounding is what floating point does, so it is said rather than refused.
    /// </summary>
    private static ZodExpression Number(IrScalar scalar)
    {
        var code = Facets.Number(scalar, json: true);
        if (scalar.Format == IrFormat.Float)
            return new Noted(code, new ZodNote($"{scalar.Sql ?? "The number"} is binary floating point, so a value is sent as the nearest one it holds."));
        return scalar.Facets?.Digits is { Precision: > 15 } digits
            ? new Noted(code, new ZodNote($"{scalar.Sql ?? "The decimal"} has {digits.Precision.ToString(CultureInfo.InvariantCulture)} digits, more than a JavaScript number keeps, so a value of more than 15 digits is sent as the nearest double.", ZodCaveat.Doubles))
            : code;
    }
}
