using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference.Typing;

/// <summary>
/// The server's rules for the bitwise operators (<c>&amp;</c>, <c>|</c>, <c>^</c>, <c>~</c>), the shifts (<c>&lt;&lt;</c>,
/// <c>&gt;&gt;</c>, SQL Server 2022) and string concatenation with <c>||</c> (SQL Server 2025): the type of the result, or null for a
/// pair the server refuses (402 or 8117) or one not modelled. Each was observed over every pair of a grid of types
/// (BitwiseGridTests) and with values
/// (<c>Bitwise_operators_shifts_and_concatenation_are_null_when_an_operand_is_and_never_overflow</c>); README.md in the project,
/// "Bitwise operators, shifts and the concatenation operator", says what they rest on.
/// </summary>
internal static class Bitwise
{
    /// <summary>
    /// <c>a &amp; b</c>, <c>a | b</c>, <c>a ^ b</c>: two integers give the wider; a bit beside an integer the integer, two bits a bit;
    /// a string beside an integer or a bit is converted to that type, which it is. Anything else is refused (402): two strings, a
    /// decimal, money, a float, a date or a GUID.
    /// </summary>
    public static ScalarType? Logical(ScalarType left, ScalarType right)
    {
        static bool Bits(ScalarType t) => t.IsInteger || t.IsBit;
        return (left, right) switch
        {
            ({ IsInteger: true }, { IsInteger: true }) => left.IntegerDigits >= right.IntegerDigits ? left : right,
            ({ IsBit: true }, _) when Bits(right) => right,
            (_, { IsBit: true }) when Bits(left) => left,
            ({ IsString: true }, _) when Bits(right) => right,
            (_, { IsString: true }) when Bits(left) => left,
            _ => null,
        };
    }

    /// <summary>
    /// <c>a &lt;&lt; n</c> and <c>a &gt;&gt; n</c>: a's type, when both are integers (not a bit, not a string). A shift past the type's
    /// width gives 0, a negative one shifts the other way, and <c>&gt;&gt;</c> fills with zeros, so nothing overflows.
    /// </summary>
    public static ScalarType? Shifted(ScalarType left, ScalarType right) => left.IsInteger && right.IsInteger ? left : null;

    /// <summary><c>~a</c>: a's type, when it is an integer or a bit; anything else is refused (8117).</summary>
    public static ScalarType? Not(ScalarType operand) => operand.IsInteger || operand.IsBit ? operand : null;

    /// <summary>
    /// <c>a || b</c>: a varying string, national when either is, max when either is, else as long as both together up to 8000
    /// bytes (4000 characters for nvarchar), where at least one is a string. Another type is written as text as long as its
    /// longest (<see cref="TextLength"/>). Two values that are not strings, and a bit beside anything, are refused (402).
    /// </summary>
    public static ScalarType? Concatenated(ScalarType left, ScalarType right)
    {
        if (!left.IsString && !right.IsString)
            return null;
        if (TextLength(left) is not { } l || TextLength(right) is not { } r)
            return null;
        var max = left.IsMax || right.IsMax;
        return ScalarType.CharacterString(left.IsNational || right.IsNational, varying: true, max ? -1 : l + r);
    }

    /// <summary>
    /// The length <c>||</c> gives a value as text (observed with <c>sp_describe_first_result_set</c>, the same whatever the
    /// precision, scale or fractional digits): a string its own; tinyint 4, smallint 6, int 12, bigint 24; decimal and numeric 41;
    /// money and smallmoney 40; float and real 23; every date and time type, and uniqueidentifier, 40. Null for a bit, which is
    /// refused, and any type not listed.
    /// </summary>
    private static int? TextLength(ScalarType type) => type switch
    {
        { IsString: true } => type.Length,
        { Type: var t } when t == BuiltInType.TinyInt => 4,
        { Type: var t } when t == BuiltInType.SmallInt => 6,
        { Type: var t } when t == BuiltInType.Int => 12,
        { Type: var t } when t == BuiltInType.BigInt => 24,
        { IsDecimal: true } => 41,
        { IsMoney: true } => 40,
        { IsApproximate: true } => 23,
        { IsDateOrTime: true } => 40,
        { Family: TypeFamily.Uuid } => 40,
        _ => null,
    };
}
