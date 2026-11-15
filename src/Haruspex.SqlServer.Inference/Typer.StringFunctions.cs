using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The string functions that read characters or codes, or build a string from a count: ASCII to DIFFERENCE. Each is typed for
// string arguments (and integer counts, codes and positions) only; the server converts other values to strings first, at
// lengths of its own, which is not modelled. README.md in this folder ("Strings") gives the rules, observed in
// FunctionObservationTests.Strings.cs.
internal sealed partial class Typer
{
    /// <summary>
    /// Why a function given an argument it can't use (a code, a position, a count or a length outside what it reads) is NULL:
    /// <c>out-of-range</c>, at the call.
    /// </summary>
    private IrNullReason OutOfRangeAt(TSqlFragment call) => new(IrNullRule.OutOfRange, At(call).ToString());

    /// <summary>A value's integer type, when it is an integer.</summary>
    private static ScalarType? IntegerOf(Value value) =>
        TypeOf(value) is { IsInteger: true } type ? type : null;

    /// <summary>
    /// <c>ASCII(s)</c> and <c>UNICODE(s)</c>: int, the code of s's first character, NULL when s is empty (observed:
    /// Ascii_and_unicode_are_int_and_null_for_an_empty_string), unless s is a literal that isn't.
    /// </summary>
    private Value FirstCode(FunctionCall call, ReadScope scope)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (StringOf(value) is null)
            return NotModelled(call, "a code of a value that is not a string is not modelled.");
        var empty = LiteralOf(call.Parameters[0]) is not ({ } literal and StringLiteral, _) || literal.Value.Length == 0;
        return Typed(ScalarType.Of(BuiltInType.Int), empty ? NullReasons.Join(value.NullableBecause, [OutOfRangeAt(call)]) : value.NullableBecause, call);
    }

    /// <summary>
    /// <c>CHAR(n)</c>: char(1), NULL when n is outside 0 to 255 (observed:
    /// Char_is_char_1_and_null_outside_0_to_255_and_overflows_past_int), which a tinyint and a literal in range can't be. A bigint
    /// is converted to int first, which may overflow.
    /// </summary>
    private Value CharOf(FunctionCall call, ReadScope scope)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (IntegerOf(value) is not { } type)
            return NotModelled(call, "CHAR of a value that is not an integer is not modelled.");
        var because = value.NullableBecause;
        if (NumberOf(call.Parameters[0]) is not (>= 0 and <= 255) && type.Type != BuiltInType.TinyInt)
            because = NullReasons.Join(because, [OutOfRangeAt(call)]);
        if (type.Type == BuiltInType.BigInt && NumberOf(call.Parameters[0]) is null)
            because = NullReasons.Join(because, Overflow(call));
        return Typed(ScalarType.CharacterString(national: false, varying: false, 1), because, call);
    }

    /// <summary>
    /// <c>CHARINDEX(find, s [, start])</c> and <c>PATINDEX(pattern, s)</c>: int, bigint when s is max; NULL only when an argument
    /// is (observed: Charindex_and_patindex_are_int_or_bigint_over_max_and_null_only_for_a_null_argument).
    /// </summary>
    private Value Position(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is null || StringOf(values[1]) is not { } searched || (values.Count == 3 && IntegerOf(values[2]) is null))
            return NotModelled(call, "a position in a value that is not a string is not modelled.");
        return Typed(ScalarType.Of(searched.IsMax ? BuiltInType.BigInt : BuiltInType.Int), Reasons(values), call);
    }

    /// <summary>
    /// <c>REPLICATE(s, n)</c>: s's type varying, as long as s times a literal n (from 1) up to 8000 bytes, or 8000 bytes when n
    /// isn't a literal; max staying max. NULL when n is negative (observed:
    /// Replicate_and_space_are_as_long_as_a_literal_count_allows_up_to_8000_bytes_and_null_for_a_negative_one). The literal 0 is
    /// described oddly (varchar(2), whatever s) and is not modelled.
    /// </summary>
    private Value Replicate(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is not { } type || IntegerOf(values[1]) is null)
            return NotModelled(call, "REPLICATE of a value that is not a string, or by a count that is not an integer, is not modelled.");
        var most = type.Type.IsNational ? BuiltInType.MostBytes / 2 : BuiltInType.MostBytes;
        var count = NumberOf(call.Parameters[1]);
        if (count is <= 0)
            return NotModelled(call, "REPLICATE by a count of 0 or less is not modelled.");
        var length = type.IsMax ? -1 : count is { } n ? (int)Math.Min(type.Length * n, most) : most;
        var because = count is null ? NullReasons.Join(Reasons(values), [OutOfRangeAt(call)]) : Reasons(values);
        return Typed(ScalarType.CharacterString(type.IsNational, varying: true, length), because, call);
    }

    /// <summary>
    /// <c>SPACE(n)</c>: varchar of a literal n (at least 1) up to 8000, or 8000 when n isn't a literal; NULL when n is negative
    /// (observed in the same test as <c>REPLICATE</c>). A bigint is not modelled.
    /// </summary>
    private Value Space(FunctionCall call, ReadScope scope)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (IntegerOf(value) is not { } type || type.Type == BuiltInType.BigInt)
            return NotModelled(call, "SPACE of a value that is not an int is not modelled.");
        var count = NumberOf(call.Parameters[0]);
        if (count is < 0)
            return NotModelled(call, "SPACE of a negative count is not modelled.");
        var length = count is { } n ? (int)Math.Clamp(n, 1, BuiltInType.MostBytes) : BuiltInType.MostBytes;
        var because = count is null ? NullReasons.Join(value.NullableBecause, [OutOfRangeAt(call)]) : value.NullableBecause;
        return Typed(ScalarType.CharacterString(national: false, varying: true, length), because, call);
    }

    /// <summary>
    /// <c>STUFF(s, start, length, r)</c>: varying, nvarchar when s or r is, max when either is; otherwise as long as both less
    /// what a literal length deletes from a literal start, or 8000 bytes. NULL when s, start or length is, and for a start of 0 or
    /// past s's end, a negative length or an empty s, none of which the types rule out; a NULL r only deletes (observed:
    /// Stuff_is_as_long_as_its_strings_less_a_literal_length_and_null_for_a_start_or_length_it_cannot_use).
    /// </summary>
    private Value Stuff(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        var replacement = Strip(call.Parameters[3]) is NullLiteral ? null : StringOf(values[3]);
        if (StringOf(values[0]) is not { } text || IntegerOf(values[1]) is null || IntegerOf(values[2]) is null
            || (replacement is null && Strip(call.Parameters[3]) is not NullLiteral))
        {
            return NotModelled(call, "STUFF of a value that is not a string, or at a position that is not an integer, is not modelled.");
        }

        var national = text.IsNational || replacement is { IsNational: true };
        var most = national ? BuiltInType.MostBytes / 2 : BuiltInType.MostBytes;
        int length;
        if (text.IsMax || replacement is { IsMax: true })
        {
            length = -1;
        }
        else if (NumberOf(call.Parameters[1]) is { } start && NumberOf(call.Parameters[2]) is { } count)
        {
            // What is deleted is at most what is left from the start: STUFF('abc', 2, 5, 'xyz') is as long as 'a' and 'xyz'
            // (observed by the sweep's grid). A start outside the string, or a negative count, gives NULL, of a type not observed.
            if (replacement is null || start < 1 || start > text.Length || count < 0)
                return NotModelled(call, "STUFF at a position outside its string is not modelled.");
            var sum = text.Length - Math.Min(count, text.Length - start + 1) + replacement.Length;
            if (sum < 1 || sum > most)
                return NotModelled(call, "this length of STUFF's result is not modelled.");
            length = (int)sum;
        }
        else
        {
            length = most;
        }

        return Typed(ScalarType.CharacterString(national, varying: true, length), NullReasons.Join(Reasons(values.Take(3)), [OutOfRangeAt(call)]), call);
    }

    /// <summary>
    /// <c>QUOTENAME(s [, quote])</c>: nvarchar(258), NULL when s is longer than 128 characters or the quote isn't one it reads
    /// (observed: Quotename_is_nvarchar_258_and_null_past_128_characters_or_for_a_quote_it_cannot_use), unless s is a literal of
    /// 128 characters or fewer and there is no quote.
    /// </summary>
    private Value QuoteName(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (values.Any(v => StringOf(v) is null))
            return NotModelled(call, "QUOTENAME of a value that is not a string is not modelled.");
        var fits = values.Count == 1 && LiteralOf(call.Parameters[0]) is ({ } literal and StringLiteral, _) && literal.Value.Length <= 128;
        return Typed(new ScalarType(BuiltInType.NVarChar, 258), fits ? Reasons(values) : NullReasons.Join(Reasons(values), [OutOfRangeAt(call)]), call);
    }

    /// <summary>
    /// <c>STR(x [, length [, decimals]])</c> of a number: varchar(10), or of a literal length (at least 1), or 8000 when the
    /// length isn't a literal. NULL when the length isn't one from 0 to 8000 or the decimals aren't a literal; a number too long
    /// for the length gives stars, not NULL (observed:
    /// Str_is_varchar_of_its_literal_length_10_by_default_and_null_for_a_length_it_cannot_use).
    /// </summary>
    private Value Str(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (TypeOf(values[0]) is not { IsNumber: true } || values.Skip(1).Any(v => IntegerOf(v) is null))
            return NotModelled(call, "STR of a value that is not a number, or with a length that is not an integer, is not modelled.");
        var given = call.Parameters.Count > 1 ? NumberOf(call.Parameters[1]) : 10;
        if (given is < 0 or > 8000)
            return NotModelled(call, "STR of a length outside 0 to 8000 is not modelled.");
        var length = given is { } n ? (int)Math.Max(n, 1) : BuiltInType.MostBytes;
        var exact = given is not null && (call.Parameters.Count < 3 || NumberOf(call.Parameters[2]) is >= 0);
        return Typed(ScalarType.CharacterString(national: false, varying: true, length), exact ? Reasons(values) : NullReasons.Join(Reasons(values), [OutOfRangeAt(call)]), call);
    }

    /// <summary>
    /// <c>TRANSLATE(s, from, to)</c>: varchar(8000), nvarchar(4000) when any argument is national, max only when s is; NULL when
    /// an argument is; strings of different lengths are an error (observed:
    /// Translate_is_8000_bytes_unless_its_string_is_max_and_soundex_and_difference_are_fixed).
    /// </summary>
    private Value Translate(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        var types = values.Select(StringOf).OfType<ScalarType>().ToList();
        if (types.Count < values.Count)
            return NotModelled(call, "TRANSLATE of a value that is not a string is not modelled.");
        var national = types.Any(t => t.IsNational);
        return Typed(ScalarType.CharacterString(national, varying: true, types[0].IsMax ? -1 : national ? BuiltInType.MostBytes / 2 : BuiltInType.MostBytes), Reasons(values), call);
    }

    /// <summary>
    /// <c>SOUNDEX(s)</c>, varchar(5), and <c>DIFFERENCE(a, b)</c>, int, of strings: NULL only when an argument is (observed in
    /// the same test as <c>TRANSLATE</c>).
    /// </summary>
    private Value Phonetic(FunctionCall call, ScalarType type, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (values.Any(v => StringOf(v) is null))
            return NotModelled(call, "SOUNDEX or DIFFERENCE of a value that is not a string is not modelled.");
        return Typed(type, Reasons(values), call);
    }
}
