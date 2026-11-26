namespace Haruspex.DifferentialTests;

// A write's OUTPUT through a CTE or a derived table, the queue-claim idiom: which such writes SQL Server takes, what their
// inserted and deleted are, and how many rows TOP lets them return.
public partial class DmlObservationTests
{
    [Fact]
    public async Task Output_through_a_cte_or_derived_table_is_the_columns_it_names_typed_as_the_table_declares_them()
    {
        string[] setup = [Item, Seed];

        // inserted and deleted are rows of the CTE: named as it names its columns, a star listing only those, each the table's
        // column as declared (int not null, nvarchar(10) null), not as the CTE's WHERE found it before the write.
        const string Described = """
            SELECT CONCAT(name, ' ', system_type_name, ' ', is_nullable) AS c FROM sys.dm_exec_describe_first_result_set(N'
              WITH c (n, tag) AS (SELECT id, label FROM dbo.obs_item WHERE label IS NOT NULL)
              UPDATE c SET tag = NULL OUTPUT inserted.*, deleted.tag AS was;', NULL, 0) ORDER BY column_ordinal;
            """;
        Assert.Equal("c: n int 0;tag nvarchar(10) 1;was nvarchar(10) 1", (await Run(setup, Described)).ToString());
        Assert.Equal("n,tag,was: 1,null,one", (await Run(setup,
            "WITH c (n, tag) AS (SELECT id, label FROM dbo.obs_item WHERE label IS NOT NULL) UPDATE c SET tag = NULL OUTPUT inserted.*, deleted.tag AS was;")).ToString());

        // The derived table's form, and a CTE of a CTE: the same.
        Assert.Equal("id,label,qty: 2,taken,2", (await Run(setup,
            "UPDATE p SET label = N'taken' OUTPUT inserted.* FROM (SELECT TOP (1) * FROM dbo.obs_item WHERE label IS NULL ORDER BY id) AS p;")).ToString());
        Assert.Equal("z,x: zz,1", (await Run(setup,
            "WITH a (x, y) AS (SELECT id, label FROM dbo.obs_item), b AS (SELECT TOP (1) y AS z, x FROM a ORDER BY x) UPDATE b SET z = N'zz' OUTPUT inserted.*;")).ToString());

        // A column of the table the CTE leaves out, or names otherwise, is not a column of inserted.
        Assert.Equal(207, (await Run(setup, "WITH c AS (SELECT id, label FROM dbo.obs_item) UPDATE c SET label = N'x' OUTPUT inserted.qty;")).Error);
        Assert.Equal(207, (await Run(setup, "WITH c (n, tag) AS (SELECT id, label FROM dbo.obs_item) UPDATE c SET tag = N'x' OUTPUT inserted.id;")).Error);

        // DELETE, INSERT and MERGE through a CTE of one table, likewise.
        Assert.Equal("id,label: 1,one", (await Run(setup, "WITH c AS (SELECT TOP (1) * FROM dbo.obs_item ORDER BY id) DELETE c OUTPUT deleted.id, deleted.label;")).ToString());
        Assert.Equal("id,label,qty: 3,null,0", (await Run(setup, "WITH c AS (SELECT * FROM dbo.obs_item) INSERT INTO c (id) OUTPUT inserted.* VALUES (3);")).ToString());

        // An INSERT's row need not meet the CTE's WHERE: label is null although the CTE keeps only rows where it isn't.
        Assert.Equal("id,label: 3,null", (await Run(setup, "WITH c AS (SELECT id, label FROM dbo.obs_item WHERE label IS NOT NULL) INSERT INTO c (id) OUTPUT inserted.id, inserted.label VALUES (3);")).ToString());
        Assert.Equal("$action,id,label: UPDATE,1,m", (await Run(setup,
            "WITH c AS (SELECT id, label FROM dbo.obs_item) MERGE c USING (VALUES (1)) AS s (id) ON c.id = s.id WHEN MATCHED THEN UPDATE SET label = N'm' OUTPUT $action, inserted.*;")).ToString());

        // A computed column of the CTE is allowed in OUTPUT, as its expression over the row after the write (11 + 1), typed as
        // that expression is: haruspex leaves such a source unknown, which only claims less.
        const string Computed = """
            SELECT CONCAT(name, ' ', system_type_name) AS c FROM sys.dm_exec_describe_first_result_set(N'
              WITH c AS (SELECT id, qty, CAST(label AS nvarchar(50)) AS wide, qty + 1 AS more FROM dbo.obs_item)
              UPDATE c SET qty = 11 OUTPUT inserted.wide, inserted.more;', NULL, 0) ORDER BY column_ordinal;
            """;
        Assert.Equal("c: wide nvarchar(50);more int", (await Run(setup, Computed)).ToString());
        Assert.Equal("wide,more: one,12", (await Run(setup,
            "WITH c AS (SELECT id, qty, CAST(label AS nvarchar(50)) AS wide, qty + 1 AS more FROM dbo.obs_item) UPDATE c SET qty = 11 OUTPUT inserted.wide, inserted.more WHERE id = 1;")).ToString());
    }

    [Fact]
    public async Task Writes_through_a_cte_of_a_join_an_aggregate_distinct_or_a_set_operation_are_refused_or_reach_another_table()
    {
        string[] setup = [Item, Seed, Note, "INSERT INTO dbo.obs_note (text) VALUES (N'n1'), (NULL);"];
        const string Joined = "WITH c AS (SELECT i.id, i.label, n.text FROM dbo.obs_item AS i JOIN dbo.obs_note AS n ON n.id = i.id)";

        // A join: the write reaches one of its tables, which only the SET says, and refuses a change to both (4405); a star
        // over inserted reaches the other table's column, which is refused (404).
        Assert.Equal("id,label: 1,j;2,j", (await Run(setup, $"{Joined} UPDATE c SET label = N'j' OUTPUT inserted.id, inserted.label;")).ToString());
        Assert.Equal(4405, (await Run(setup, $"{Joined} UPDATE c SET label = N'j', text = N'k';")).Error);
        Assert.Equal(4405, (await Run(setup, $"{Joined} DELETE c;")).Error);
        Assert.Equal(404, (await Run(setup, $"{Joined} UPDATE c SET label = N'j' OUTPUT inserted.*;")).Error);

        // An aggregate or DISTINCT (4403), a set operation or a write to a column with COLLATE (4406), a VALUES list (4421).
        Assert.Equal(4403, (await Run(setup, "WITH c AS (SELECT label, COUNT(*) AS k FROM dbo.obs_item GROUP BY label) UPDATE c SET label = N'g';")).Error);
        Assert.Equal(4403, (await Run(setup, "WITH c AS (SELECT DISTINCT label FROM dbo.obs_item) UPDATE c SET label = N'd';")).Error);
        Assert.Equal(4406, (await Run(setup, "WITH c AS (SELECT id, label FROM dbo.obs_item UNION ALL SELECT id, text FROM dbo.obs_note) UPDATE c SET label = N'u';")).Error);
        Assert.Equal(4406, (await Run(setup, "WITH c AS (SELECT id, label COLLATE Latin1_General_BIN2 AS label FROM dbo.obs_item) UPDATE c SET label = N'q' OUTPUT inserted.label;")).Error);
        Assert.Equal(4421, (await Run(setup, "UPDATE p SET x = 1 FROM (VALUES (1)) AS p (x);")).Error);
    }

    [Fact]
    public async Task Top_1_caps_the_output_of_a_write_and_of_an_update_or_delete_through_a_cte_but_not_an_insert_or_merge_through_it()
    {
        string[] setup = [Item, Seed, Note, "INSERT INTO dbo.obs_note (text) VALUES (N'n1'), (N'n2'), (N'n3');"];

        // An UPDATE or DELETE through a TOP (1) source changes at most its one row, even joined to several rows in FROM.
        Assert.Equal("id: 1", (await Run(setup, "WITH c AS (SELECT TOP (1) * FROM dbo.obs_item ORDER BY id) UPDATE c SET qty = 0 OUTPUT inserted.id;")).ToString());
        Assert.Equal("id: 1", (await Run(setup, "WITH c AS (SELECT * FROM dbo.obs_item ORDER BY id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY) DELETE c OUTPUT deleted.id;")).ToString());
        Assert.Equal("id: 1", (await Run(setup,
            "UPDATE p SET qty = 0 OUTPUT inserted.id FROM (SELECT TOP (1) * FROM dbo.obs_item ORDER BY id) AS p JOIN dbo.obs_note AS n ON n.id >= p.id;")).ToString());

        // An INSERT through it inserts every row, and a MERGE inserts past it.
        Assert.Equal("id: 1;2;3", (await Run([Item, Note, "INSERT INTO dbo.obs_note (text) VALUES (N'n1'), (N'n2'), (N'n3');"],
            "WITH c AS (SELECT TOP (1) id, label FROM dbo.obs_item ORDER BY id) INSERT INTO c (id) OUTPUT inserted.id SELECT id FROM dbo.obs_note;")).ToString());
        Assert.Equal("$action,id: UPDATE,1;INSERT,3", (await Run(setup,
            "WITH c AS (SELECT TOP (1) id, label FROM dbo.obs_item ORDER BY id) MERGE c USING (VALUES (1), (3)) AS s (id) ON c.id = s.id WHEN MATCHED THEN UPDATE SET label = N'm' WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) OUTPUT $action, inserted.id;")).ToString());

        // The write's own TOP (1) caps every write.
        Assert.Equal("id: 1", (await Run(setup, "UPDATE TOP (1) i SET qty = 0 OUTPUT inserted.id FROM dbo.obs_item AS i JOIN dbo.obs_note AS n ON n.id >= i.id WHERE i.id = 1;")).ToString());
        Assert.Single((await Run(setup, "DELETE TOP (1) FROM dbo.obs_item OUTPUT deleted.id;")).Sets[0].Split(';'));
        Assert.Single((await Run([Item, Note, "INSERT INTO dbo.obs_note (text) VALUES (N'n1'), (N'n2');"],
            "INSERT TOP (1) INTO dbo.obs_item (id) OUTPUT inserted.id SELECT id FROM dbo.obs_note;")).Sets[0].Split(';'));
        Assert.Single((await Run(setup,
            "MERGE TOP (1) dbo.obs_item AS t USING (VALUES (1), (2), (9)) AS s (id) ON t.id = s.id WHEN MATCHED THEN UPDATE SET qty = 0 WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) OUTPUT inserted.id;")).Sets[0].Split(';'));
    }
}
