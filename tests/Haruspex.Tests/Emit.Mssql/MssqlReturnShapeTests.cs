using Haruspex.Emit.Mssql;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Mssql;

/// <summary>
/// What a procedure's function hands back (<see cref="MssqlReturnShape"/>), as the module writes it (decision 0060), and whether
/// callers written against one shape still read another: what <c>haruspex check --baseline</c> warns of.
/// </summary>
public class MssqlReturnShapeTests
{
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static readonly IrResult Rows = new(IrEncoding.Rowset, new IrArray(Int), []);

    private static IrObject Procedure(IrParameter[] parameters, bool returnsCode, params IrResult[] results) =>
        new("shop.get_order", "GetOrder", IrObjectKind.Procedure, "p.sql:1:1", [.. parameters], [.. results]) { ReturnsCode = returnsCode };

    private static readonly IrParameter Output = new("@lines", Int, true, true);

    [Fact]
    public void Each_shape_is_named_as_the_module_writes_it()
    {
        Assert.Equal("the result itself", MssqlEmitter.ReturnShape(Procedure([], false, Rows)).Description);
        Assert.Equal("{ result1, result2 }", MssqlEmitter.ReturnShape(Procedure([], false, Rows, Rows)).Description);
        Assert.Equal("{ result, output, returnValue }", MssqlEmitter.ReturnShape(Procedure([Output], true, Rows)).Description);
        Assert.Equal("nothing", MssqlEmitter.ReturnShape(Procedure([], false)).Description);
    }

    [Fact]
    public void A_result_returned_itself_that_becomes_an_object_loses_its_callers_and_a_field_gained_keeps_them()
    {
        var alone = MssqlEmitter.ReturnShape(Procedure([], false, Rows));
        var two = MssqlEmitter.ReturnShape(Procedure([], false, Rows, Rows));
        var withOutput = MssqlEmitter.ReturnShape(Procedure([Output], false, Rows, Rows));

        Assert.False(two.KeepsCallersOf(alone));
        Assert.False(alone.KeepsCallersOf(two));
        Assert.True(withOutput.KeepsCallersOf(two));
        Assert.False(two.KeepsCallersOf(withOutput));
        Assert.True(alone.KeepsCallersOf(MssqlEmitter.ReturnShape(Procedure([], false))));
    }
}
