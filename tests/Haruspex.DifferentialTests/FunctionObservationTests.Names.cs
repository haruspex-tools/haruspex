namespace Haruspex.DifferentialTests;

// The functions that read or give an object's or a database's name: PARSENAME and DB_NAME. Their types, and when each is NULL.
// Inference's README.md, "Names", gives the rules these rest on.
public partial class FunctionObservationTests
{
    [Fact]
    public async Task Parsename_is_nvarchar_128_and_null_for_a_part_it_cannot_find_or_a_name_it_cannot_read()
    {
        // nvarchar(128) whatever the name's type and length, a fixed-length or max one included, and whatever the part's type.
        Assert.Equal(
            "a nvarchar(128), b nvarchar(128), c nvarchar(128), d nvarchar(128), e nvarchar(128), f nvarchar(128)",
            await Describe($"""
                SELECT PARSENAME(s, 1) AS a, PARSENAME(ns, 2) AS b, PARSENAME(c, 1) AS c, PARSENAME(vm, ti) AS d, PARSENAME(nm, bi) AS e,
                       PARSENAME(N'a.b.c', i) AS f
                FROM {Row}
                """));

        // NULL for a part past the name's or outside 1 to 4, an empty part, more than four parts, a part past 128 characters, an
        // opening bracket not closed, and for a NULL name or part. A bracketed part keeps its dots.
        Assert.Equal(
            "a,b,c,d,e,f,g,h,i,j,k,l,m,n: c,null,null,null,null,null,null,null,null,a.b,null,null,null,x",
            await Select("""
                DECLARE @s nvarchar(10) = NULL, @p int = NULL;
                SELECT PARSENAME('a.b.c', 1) AS a, PARSENAME('a.b.c', 4) AS b, PARSENAME('a.b', 0) AS c, PARSENAME('a', -1) AS d,
                       PARSENAME('a..c', 2) AS e, PARSENAME('a.b.c.d.e', 1) AS f, PARSENAME(REPLICATE('x', 129), 1) AS g,
                       PARSENAME('[a', 1) AS h, PARSENAME('', 1) AS i, PARSENAME('[a.b].c', 2) AS j, PARSENAME(@s, 1) AS k,
                       PARSENAME('a', @p) AS l, PARSENAME('a.', 1) AS m, PARSENAME(REPLICATE('x', 127) + '.x', 1) AS n;
                """));

        // The part is converted to int: a wider one may overflow, an error by default and NULL under the caller's options.
        const string Wide = "SELECT PARSENAME(N'a', CAST(2147483648 AS bigint)) AS v;";
        Assert.Equal(" ! 8115", (await Run([], Wide)).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Wide)).ToString());
    }

    [Fact]
    public async Task Db_name_is_nvarchar_128_never_null_for_the_current_database_and_null_for_an_id_the_caller_cannot_see()
    {
        Assert.Equal("a nvarchar(128), b nvarchar(128), c nvarchar(128)", await Describe($"SELECT DB_NAME() AS a, DB_NAME(i) AS b, DB_NAME(ti) AS c FROM {Row}"));

        // DB_NAME() names the current database, to a user granted nothing too; so does DB_NAME(0). An id is NULL when it names
        // no database, or one the caller can't see: model (3) and msdb (4) are hidden from a user granted nothing, while master
        // (1) and tempdb (2) are not.
        Assert.Equal(
            "a,b,c,d,e,f,g,h: 1,1,null,null,null,master,tempdb,null",
            (await Run(["CREATE USER obs_nobody WITHOUT LOGIN;"], """
                DECLARE @n int = NULL;
                EXECUTE AS USER = 'obs_nobody';
                SELECT IIF(DB_NAME() = DB_NAME(DB_ID()), 1, 0) AS a, IIF(DB_NAME(0) = DB_NAME(), 1, 0) AS b, DB_NAME(3) AS c, DB_NAME(4) AS d,
                       DB_NAME(32767) AS e, DB_NAME(1) AS f, DB_NAME(2) AS g, DB_NAME(@n) AS h;
                REVERT;
                """)).ToString());

        // The id is converted to int: a wider one may overflow, an error by default and NULL under the caller's options.
        const string Wide = "SELECT DB_NAME(CAST(3000000000 AS bigint)) AS v;";
        Assert.Equal(" ! 8115", (await Run([], Wide)).ToString());
        Assert.Equal("v: null", (await Run([], "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; " + Wide)).ToString());
    }
}
