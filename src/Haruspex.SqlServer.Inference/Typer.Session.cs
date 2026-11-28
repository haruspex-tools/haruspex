using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The values a call reads from its session or its database that take arguments or a name: IDENT_CURRENT, USER_NAME of an id and
// NEXT VALUE FOR. Each has the type the server fixes and is nullable unless it is never NULL in any state (decision 0146).
// README.md in this folder ("Session values") gives the rules, observed in FunctionObservationTests.Session.cs.
internal sealed partial class Typer
{
    /// <summary>
    /// <c>IDENT_CURRENT(name)</c> of a string: numeric(38,0), always <c>out-of-range</c>, since it is NULL for a name that names no
    /// table, a table without an identity, and a table the caller isn't allowed to see, which depends on the caller, not the
    /// project (observed: Ident_current_is_numeric_38_0_and_null_for_a_table_without_an_identity_a_missing_one_or_one_the_caller_cannot_see).
    /// </summary>
    private Value IdentCurrent(FunctionCall call, ReadScope scope)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (StringOf(value) is null)
            return NotModelled(call, "IDENT_CURRENT of a value that is not a string is not modelled.");
        return Typed(Identity, NullReasons.Join(value.NullableBecause, [OutOfRangeAt(call)]), call);
    }

    /// <summary>
    /// <c>USER_NAME(id)</c> of an integer: nvarchar(128), <c>out-of-range</c>, NULL for an id that names no user of the database.
    /// The id is converted to int, so a wider one may overflow (observed:
    /// User_name_of_an_id_is_nvarchar_128_and_null_for_an_id_that_names_no_user).
    /// </summary>
    private Value UserName(FunctionCall call, ReadScope scope)
    {
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (IntegerOf(value) is null)
            return NotModelled(call, "USER_NAME of a value that is not an integer is not modelled.");
        var because = NullReasons.Join(value.NullableBecause, [OutOfRangeAt(call)]);
        if (!FitsInt(value, call.Parameters[0]))
            because = NullReasons.Join(because, Overflow(call));
        return Typed(Sysname, because, call);
    }

    /// <summary>
    /// <c>NEXT VALUE FOR sequence</c>: the sequence's type, an alias type's base type, never null, since a sequence that has
    /// given its last value fails (11728) rather than giving NULL, under any options (observed:
    /// Next_value_for_is_its_sequences_type_never_null_and_an_error_once_a_sequence_without_cycle_is_spent). A sequence the
    /// model doesn't hold, or one whose type it can't resolve, is not modelled; so is <c>OVER</c>.
    /// </summary>
    private Value NextValueFor(NextValueForExpression next)
    {
        if (next.OverClause is not null)
            return NotModelled(next, "NEXT VALUE FOR with OVER is not modelled.");
        if (binder.ResolveSequence(next.SequenceName) is not { } sequence)
            return NotModelled(next, $"{Text(next.SequenceName)} is not a sequence of the project, or which one it is depends on the caller.");
        if (SchemaRules.ResolveType(model, sequence.Type)?.Scalar is not { } type)
            return NotModelled(next, $"the type of {sequence.Name} is not known.");
        return Typed(type, [], next);
    }

    /// <summary>The type of an identity the server reports, whatever the column's own type: numeric(38,0).</summary>
    private static ScalarType Identity => new(BuiltInType.Numeric, Precision: 38, Scale: 0);
}
