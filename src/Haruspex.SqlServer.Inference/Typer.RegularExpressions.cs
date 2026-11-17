using System.Collections.Immutable;
using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The scalar regular-expression functions of SQL Server 2025: REGEXP_REPLACE, REGEXP_SUBSTR, REGEXP_INSTR and REGEXP_COUNT. They
// are there at every compatibility level from 100, so no level is checked; REGEXP_LIKE, a predicate, needs 170 and types
// nothing. What each argument may be is checked when the procedure is created, and a call the server refuses is not modelled
// (RegexpArgument). README.md in this folder ("Regular expressions") gives the rules, observed in
// FunctionObservationTests.RegularExpressions.cs.
internal sealed partial class Typer
{
    /// <summary>What an argument of a regular-expression function is, by position, which decides what the server accepts.</summary>
    private enum RegexpArgument
    {
        /// <summary>The string searched: any string, fixed-length and max included; each function reads it first.</summary>
        Text,

        /// <summary>The pattern: a string, not max.</summary>
        Pattern,

        /// <summary>REGEXP_REPLACE's replacement: any string; a max one makes the result max.</summary>
        Replacement,

        /// <summary>The flags: char or varchar, not max; a literal of c, i, s and m only.</summary>
        Flags,

        /// <summary>A start: smallint, int or bigint; a literal from 1.</summary>
        Start,

        /// <summary>REGEXP_SUBSTR's and REGEXP_INSTR's occurrence: as a start, a literal from 1.</summary>
        Occurrence,

        /// <summary>REGEXP_REPLACE's occurrence, 0 for every one: a literal from 0.</summary>
        EveryOccurrence,

        /// <summary>REGEXP_INSTR's return option: a literal 0 or 1.</summary>
        ReturnOption,

        /// <summary>A group: a literal from 0.</summary>
        Group,
    }

    /// <summary>What each argument of a regular-expression function is, by position.</summary>
    private static ImmutableArray<RegexpArgument> Layout(BuiltInFunction function) => function switch
    {
        _ when function == ScalarFunctions.RegexpReplace => [RegexpArgument.Text, RegexpArgument.Pattern, RegexpArgument.Replacement, RegexpArgument.Start, RegexpArgument.EveryOccurrence, RegexpArgument.Flags],
        _ when function == ScalarFunctions.RegexpSubstr => [RegexpArgument.Text, RegexpArgument.Pattern, RegexpArgument.Start, RegexpArgument.Occurrence, RegexpArgument.Flags, RegexpArgument.Group],
        _ when function == ScalarFunctions.RegexpInstr => [RegexpArgument.Text, RegexpArgument.Pattern, RegexpArgument.Start, RegexpArgument.Occurrence, RegexpArgument.ReturnOption, RegexpArgument.Flags, RegexpArgument.Group],
        _ => [RegexpArgument.Text, RegexpArgument.Pattern, RegexpArgument.Start, RegexpArgument.Flags],
    };

    /// <summary>
    /// <c>REGEXP_REPLACE(s, pattern [, replacement [, start [, occurrence [, flags]]]])</c>: varchar(8000) or nvarchar(4000) as
    /// s is, max when s or the replacement is; a longer result is cut short, not an error. NULL when any argument is (observed:
    /// Regexp_replace_is_8000_bytes_or_max_of_its_strings_kind_and_cuts_a_longer_result_short). Over a fixed-length string the
    /// server describes char(8000) or nchar(4000), a length the value doesn't have until it is stored, so that is not modelled.
    /// </summary>
    private Value RegexpReplace(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is not { } text)
            return NotModelled(call, NotAString);
        if (Refused(call, values, ScalarFunctions.RegexpReplace) is { } refused)
            return refused;
        if (!text.IsMax && !text.IsVarying)
            return NotModelled(call, "REGEXP_REPLACE of a fixed-length string is described as char(8000) or nchar(4000), which its value is not until it is stored.");
        var max = text.IsMax || (values.Count > 2 && StringOf(values[2]) is { IsMax: true });
        return Typed(ScalarType.CharacterString(text.IsNational, varying: true, max ? -1 : BuiltInType.MostBytes), RegexpReasons(call, values), call);
    }

    /// <summary>
    /// <c>REGEXP_SUBSTR(s, pattern [, start [, occurrence [, flags [, group]]]])</c>: s's type made varying, of its length, max
    /// staying max; always <c>out-of-range</c>, since it is NULL when nothing matches, for a start past the end, and for an
    /// occurrence or a group past those there are (observed: Regexp_substr_is_its_strings_length_varying_and_null_when_nothing_matches).
    /// </summary>
    private Value RegexpSubstr(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is not { } text)
            return NotModelled(call, NotAString);
        if (Refused(call, values, ScalarFunctions.RegexpSubstr) is { } refused)
            return refused;
        return Typed(ScalarType.CharacterString(text.IsNational, varying: true, text.IsMax ? -1 : text.Length), NullReasons.Join(RegexpReasons(call, values), [OutOfRangeAt(call)]), call);
    }

    /// <summary>
    /// <c>REGEXP_INSTR(s, pattern [, start [, occurrence [, return option [, flags [, group]]]]])</c> and
    /// <c>REGEXP_COUNT(s, pattern [, start [, flags]])</c>: int, over max too, 0 when nothing matches; NULL when an argument is
    /// (observed: Regexp_instr_and_regexp_count_are_int_from_0_and_null_only_for_a_null_argument). A NULL return option is taken as
    /// the default, which is not modelled: its reasons are kept, which claims less. The function table bounds it from 0.
    /// </summary>
    private Value RegexpPosition(FunctionCall call, ReadScope scope, BuiltInFunction function)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is null)
            return NotModelled(call, NotAString);
        return Refused(call, values, function) ?? Typed(ScalarType.Of(BuiltInType.Int), RegexpReasons(call, values), call);
    }

    private const string NotAString = "a regular-expression function of a value that is not a string is not modelled.";

    /// <summary>
    /// Why a call is not modelled, by its function's layout (<see cref="Layout"/>): an argument the server refuses when the
    /// procedure is created, or one of a type not read; null when every argument is read (observed:
    /// The_regular_expression_functions_but_regexp_like_are_there_at_every_compatibility_level_and_refuse_what_they_cannot_read).
    /// A literal pattern the server can't read is refused too, which is not checked: what is typed of a procedure the server
    /// never creates claims nothing.
    /// </summary>
    private Value? Refused(FunctionCall call, List<Value> values, BuiltInFunction function)
    {
        var layout = Layout(function);
        for (var i = 0; i < values.Count; i++)
        {
            if (Refused(layout[i], values[i], call.Parameters[i]) is { } why)
                return NotModelled(call, why);
        }

        return null;
    }

    /// <summary>
    /// Why a call that is read may be NULL: its arguments' reasons, and an overflow when a start, an occurrence, a return option
    /// or a group is wider than int, to which each is converted.
    /// </summary>
    private EquatableArray<IrNullReason> RegexpReasons(FunctionCall call, List<Value> values) =>
        values.Select((v, i) => IntegerOf(v) is not null && !FitsInt(v, call.Parameters[i])).Any(wide => wide)
            ? NullReasons.Join(Reasons(values), Overflow(call))
            : Reasons(values);

    /// <summary>Why an argument at its position is not modelled, the server refusing it or not, or null when it is read.</summary>
    private static string? Refused(RegexpArgument argument, Value value, ScalarExpression expression)
    {
        var type = TypeOf(value);
        var literal = NumberOf(expression);
        return argument switch
        {
            // The string searched is read by each function first, so is a string here.
            RegexpArgument.Text => null,
            RegexpArgument.Replacement => type is { IsString: true } ? null : "a replacement that is not a string is not modelled.",
            RegexpArgument.Pattern => type is { IsString: true, IsMax: false } ? null : "a pattern that is not a string is not modelled, and SQL Server refuses a max one.",
            RegexpArgument.Flags => type is { IsString: true, IsMax: false, IsNational: false }
                ? (LiteralOf(expression) is ({ } flags and StringLiteral, _) && flags.Value.Any(c => c is not ('c' or 'i' or 's' or 'm')) ? "SQL Server refuses flags other than c, i, s and m." : null)
                : "flags that are not char or varchar are not modelled, and SQL Server refuses national or max ones.",
            _ when type is not { IsInteger: true } || type.Type == BuiltInType.TinyInt => "a start, occurrence, return option or group that is not a smallint, an int or a bigint is not modelled, and SQL Server refuses a tinyint, a bit or a decimal.",
            RegexpArgument.Start or RegexpArgument.Occurrence when literal is < 1 => "SQL Server refuses a start or occurrence below 1.",
            RegexpArgument.EveryOccurrence or RegexpArgument.Group when literal is < 0 => "SQL Server refuses an occurrence or group below 0.",
            RegexpArgument.ReturnOption when literal is not (null or 0 or 1) => "SQL Server refuses a return option other than 0 or 1.",
            _ => null,
        };
    }
}
