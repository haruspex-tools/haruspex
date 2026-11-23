using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

// A CHECK of the form col IN (…) OR col IS NULL: its set is read, and the column stays nullable, as the CHECK passes NULL
// (observed: An_in_list_or_is_null_check_refuses_a_value_outside_the_set).
public partial class ConstructTests
{
    [Fact]
    public void An_in_list_or_is_null_check_gives_its_set_to_a_nullable_column()
    {
        var doc = Infer(
            ("tables.sql", "CREATE TABLE dbo.ticket (id int NOT NULL PRIMARY KEY, status varchar(10) NULL CHECK (status IS NULL OR status IN ('open', 'shut')));"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS SELECT t.status FROM dbo.ticket AS t;"));
        var status = Assert.Single(RowFields(doc));

        Assert.True(status.Nullable);
        Assert.Equal(["open", "shut"], Assert.IsType<IrScalar>(status.Type).Facets!.Values!.Items.Select(v => v.Text));
    }
}
