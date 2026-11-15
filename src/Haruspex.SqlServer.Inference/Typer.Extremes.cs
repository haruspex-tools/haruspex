using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// GREATEST and LEAST: the extreme of their values. README.md in this folder ("Built-in functions") gives the rule, observed
// in FunctionObservationTests.MathsAndLogic.cs.
internal sealed partial class Typer
{
    /// <summary>
    /// <c>GREATEST(a, b, …)</c> and <c>LEAST</c> of values of one kind: strings, varying unless every value is of one fixed-length
    /// type, as long as the longest, max taken as
    /// 8000 bytes (a longer value is an error, 8152, under any options), <c>nvarchar</c> if any is; integers as the widest;
    /// <c>float</c> and <c>real</c> as <c>float</c> if any is; any other kind when every value is of one type. NULLs are skipped,
    /// so it is NULL only when every value is (observed:
    /// Greatest_and_least_skip_nulls_and_cap_a_max_string_at_8000_bytes_which_a_longer_value_fails). Values of different kinds
    /// take the type of highest precedence by a rule of their own (an integer literal counts its own digits beside a
    /// decimal), which is not modelled.
    /// </summary>
    private Value Extreme(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        var types = values.Select(v => v.IsLiveJson ? null : TypeOf(v)).OfType<ScalarType>().ToList();
        if (types.Count < values.Count || ExtremeType(types) is not { } type)
            return NotModelled(call, "GREATEST or LEAST of values of different kinds is not modelled.");
        return Typed(type, values.All(v => v.NullableBecause.Count > 0) ? Reasons(values) : [], call);
    }

    private static ScalarType? ExtremeType(List<ScalarType> types)
    {
        if (types.All(t => t.IsString))
        {
            // Fixed-length strings keep their type when every value is of it (char(3) and char(3) give char(3)); fixed-length
            // ones of different types are not observed.
            if (types.All(t => !t.IsVarying))
                return types.All(t => t == types[0]) ? types[0] : null;

            var national = types.Any(t => t.IsNational);
            var most = national ? BuiltInType.MostBytes / 2 : BuiltInType.MostBytes;
            return ScalarType.CharacterString(national, varying: true, types.Any(t => t.IsMax) ? most : types.Max(t => t.Length));
        }

        if (types.All(t => t.IsInteger))
            return types.MaxBy(t => t.Type.Digits?.Precision ?? 0);
        if (types.All(t => t.IsApproximate))
            return ScalarType.Of(types.Any(t => t.Type == BuiltInType.Float) ? BuiltInType.Float : BuiltInType.Real);
        return types.All(t => t == types[0]) && (types[0].IsExact || types[0].IsDateOrTime || types[0].IsBit || types[0].Type == BuiltInType.UniqueIdentifier)
            ? types[0]
            : null;
    }
}
