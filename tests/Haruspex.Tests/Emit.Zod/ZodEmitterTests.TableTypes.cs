using Haruspex.Emit.Zod;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>A table-valued parameter's schema: an array of strict rows of its type, which may be left out but not be null.</summary>
public partial class ZodEmitterTests
{
    [Fact]
    public void A_table_valued_parameter_is_an_optional_array_of_strict_rows_each_column_checked_as_a_parameter_of_its_type()
    {
        var ts = ZodEmitter.Emit(Fixture.Named("till").Ir, new ZodOptions { Objects = ZodObjects.Strip }).Expected();

        // Strict whatever the objects are, every key required, NULL only where the column takes it.
        Assert.Contains("export const LineListRowSchema = z.strictObject({\n  product_id: z.int32(),\n  quantity: z.int().gt(0).max(32767),\n", ts, StringComparison.Ordinal);
        Assert.Contains(", which nvarchar(200) doesn't hold\" }).nullable(),\n})", ts, StringComparison.Ordinal);
        Assert.Contains("  lines: z.array(LineListRowSchema).optional(),\n", ts, StringComparison.Ordinal);
    }
}
