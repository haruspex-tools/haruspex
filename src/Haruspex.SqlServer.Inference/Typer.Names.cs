using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The functions that read or give an object's or a database's name: PARSENAME and DB_NAME. Each is nvarchar(128), a sysname,
// whatever it is given. README.md in this folder ("Names") gives the rules, observed in FunctionObservationTests.Names.cs.
internal sealed partial class Typer
{
    /// <summary>
    /// <c>PARSENAME(name, part)</c> of a string and an integer: nvarchar(128), always <c>out-of-range</c>, since it is NULL for a
    /// part the name doesn't have or outside 1 to 4, and for a name it can't read (an empty part, more than four parts, a part
    /// past 128 characters, a bracket not closed), which no type rules out. The part is converted to int, so a wider one may
    /// overflow (observed: Parsename_is_nvarchar_128_and_null_for_a_part_it_cannot_find_or_a_name_it_cannot_read).
    /// </summary>
    private Value ParseName(FunctionCall call, ReadScope scope)
    {
        var (values, unknown) = Arguments(call.Parameters, scope);
        if (unknown is not null)
            return unknown;
        if (StringOf(values[0]) is null || IntegerOf(values[1]) is null)
            return NotModelled(call, "PARSENAME of a value that is not a string, or of a part that is not an integer, is not modelled.");
        var because = NullReasons.Join(Reasons(values), [OutOfRangeAt(call)]);
        if (!FitsInt(values[1], call.Parameters[1]))
            because = NullReasons.Join(because, Overflow(call));
        return Typed(Sysname, because, call);
    }

    /// <summary>
    /// <c>DB_NAME()</c>: nvarchar(128), never null, since it names the database the call runs in, which every caller can see.
    /// <c>DB_NAME(id)</c> of an integer: nvarchar(128), <c>out-of-range</c>, NULL for an id that names no database or one the
    /// caller isn't allowed to see (model and msdb, to a user granted nothing), which depends on the server and the caller. The
    /// id is converted to int, so a wider one may overflow (observed:
    /// Db_name_is_nvarchar_128_never_null_for_the_current_database_and_null_for_an_id_the_caller_cannot_see).
    /// </summary>
    private Value DbName(FunctionCall call, ReadScope scope)
    {
        if (call.Parameters.Count == 0)
            return Typed(Sysname, [], call);
        var value = Expression(call.Parameters[0], scope);
        if (value.Shape is UnknownShape)
            return value;
        if (IntegerOf(value) is null)
            return NotModelled(call, "DB_NAME of a value that is not an integer is not modelled.");
        var because = NullReasons.Join(value.NullableBecause, [OutOfRangeAt(call)]);
        if (!FitsInt(value, call.Parameters[0]))
            because = NullReasons.Join(because, Overflow(call));
        return Typed(Sysname, because, call);
    }

    /// <summary>The type of a name, <c>sysname</c>: nvarchar(128).</summary>
    private static ScalarType Sysname => new(BuiltInType.NVarChar, 128);
}
