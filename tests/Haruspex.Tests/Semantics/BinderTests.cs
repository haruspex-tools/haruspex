using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// The binder on its own, without inference: what each name in a body means, by SQL Server's scoping rules. A column is
/// resolved in the innermost scope that has it, a CTE hides a table of its name, an APPLY's right side reads its left
/// input, and a reference that can't be bound for certain says why, case by case. The inference tests show what each
/// meaning does to a value; these show the meaning itself.
/// </summary>
public class BinderTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, code int NOT NULL);
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, name nvarchar(10) NOT NULL);
        GO
        CREATE TABLE dbo.shelf (shelf_id int NOT NULL, label varchar(10) NOT NULL);
        GO
        """;

    /// <summary>A procedure whose one statement is given, read with the standard tables.</summary>
    private sealed class Sample
    {
        /// <param name="statement">The procedure's one statement.</param>
        /// <param name="more">More of the schema, after the standard tables.</param>
        public Sample(string statement, string more = "")
        {
            using var dir = new TempDirectory();
            dir.File("tables.sql", Tables + more);
            dir.File("p.sql", $"CREATE PROCEDURE dbo.p AS\n{statement}");
            var read = SqlScriptFolder.Read(dir.Path);
            Assert.Empty(read.Diagnostics);
            var procedure = read.Model.FindProcedure(new ObjectName("dbo", "p"))!;
            Statement = Assert.Single(((SqlBody)procedure.Body).Statements.Statements);
            Binder = new Bindings(read.Model).For(procedure.Span);
        }

        public TSqlStatement Statement { get; }

        public Binder Binder { get; }

        /// <summary>The statement's query, bound as a statement's, after its WITH.</summary>
        public BoundQuery Query(NameEnvironment? names = null)
        {
            var select = (SelectStatement)Statement;
            var with = Binder.BindWith(select.WithCtesAndXmlNamespaces, names ?? NameEnvironment.Empty);
            return Binder.Bind(select.QueryExpression, null, with.Names);
        }

        /// <summary>What the column reference written <paramref name="text"/> means, read where it is written.</summary>
        public Resolution Resolve(string text, NameEnvironment? names = null)
        {
            var scopes = Scopes(Query(names));
            var column = Columns(Statement).Single(c => FragmentText(c) == text);
            return Binder.Resolve(column, scopes[Innermost(column)]);
        }

        /// <summary>Every query specification in a bound query, its subqueries bound inside the scope they are written in.</summary>
        private Dictionary<QuerySpecification, QueryScope> Scopes(BoundQuery query)
        {
            var scopes = new Dictionary<QuerySpecification, QueryScope>(ReferenceEqualityComparer.Instance);
            Add(query);
            return scopes;

            void Add(BoundQuery bound)
            {
                switch (bound)
                {
                    case BoundSpecification specification:
                        scopes[specification.Query] = specification.Scope;
                        foreach (var source in specification.Scope.Sources)
                        {
                            if (source.Target is RelationTarget { Relation: DerivedTable derived })
                                Add(derived.Query);
                        }

                        foreach (var subquery in Subqueries(specification.Query))
                            Add(Binder.Bind(subquery, specification.Scope, specification.Scope.Names));
                        break;
                    case BoundSetOperation operation:
                        Add(operation.First);
                        Add(operation.Second);
                        break;
                    case BoundParenthesis parenthesis:
                        Add(parenthesis.Inner);
                        break;
                }
            }
        }

        /// <summary>The innermost query specification a fragment is written in.</summary>
        private QuerySpecification Innermost(TSqlFragment fragment)
        {
            var finder = new Specifications();
            Statement.Accept(finder);
            return finder.Found.Where(q => q.StartOffset <= fragment.StartOffset && fragment.StartOffset < q.StartOffset + q.FragmentLength)
                .MinBy(q => q.FragmentLength)!;
        }

        private static string FragmentText(TSqlFragment fragment) => SqlServer.Syntax.FragmentText.OneLine(fragment);
    }

    private static List<ColumnReferenceExpression> Columns(TSqlFragment fragment)
    {
        var finder = new ColumnFinder();
        fragment.Accept(finder);
        return finder.Found;
    }

    /// <summary>The query expressions of the scalar, EXISTS and IN subqueries written directly in a query, outside its FROM.</summary>
    private static List<QueryExpression> Subqueries(QuerySpecification query)
    {
        var finder = new SubqueryFinder();
        foreach (var element in query.SelectElements)
            element.Accept(finder);
        query.WhereClause?.Accept(finder);
        return finder.Found;
    }

    private sealed class ColumnFinder : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> Found { get; } = [];

        public override void Visit(ColumnReferenceExpression node) => Found.Add(node);
    }

    private sealed class Specifications : TSqlFragmentVisitor
    {
        public List<QuerySpecification> Found { get; } = [];

        public override void Visit(QuerySpecification node) => Found.Add(node);
    }

    private sealed class SubqueryFinder : TSqlFragmentVisitor
    {
        public List<QueryExpression> Found { get; } = [];

        public override void ExplicitVisit(ScalarSubquery node) => Found.Add(node.QueryExpression);
    }

    private static T Source<T>(BoundQuery query, int index = 0)
        where T : SourceTarget => Assert.IsType<T>(((BoundSpecification)query).Scope.Sources[index].Target);

    // ---- what a column means ------------------------------------------------------------------------------------------

    [Fact]
    public void A_qualified_column_is_the_column_of_the_source_its_qualifier_names()
    {
        var column = Assert.IsType<TableColumn>(new Sample("SELECT u.name FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;").Resolve("u.name"));

        Assert.Equal("dbo.u", column.Table.Name.ToString());
        Assert.Equal("u", column.Source.Alias);
    }

    [Fact]
    public void An_unqualified_column_is_the_one_source_that_has_it()
    {
        var column = Assert.IsType<TableColumn>(new Sample("SELECT code FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;").Resolve("code"));

        Assert.Equal("dbo.t", column.Table.Name.ToString());
    }

    [Fact]
    public void A_column_the_subquery_s_sources_lack_is_found_in_the_enclosing_query()
    {
        var column = Assert.IsType<TableColumn>(new Sample("SELECT (SELECT TOP (1) code FROM dbo.u AS u) AS c FROM dbo.t AS t;").Resolve("code"));

        Assert.Equal("t", column.Source.Alias);
    }

    [Fact]
    public void A_column_the_subquery_s_own_source_has_is_not_looked_for_outside()
    {
        // Both have name: the innermost scope that has it wins, so it is u's, not ambiguous and not t's.
        var column = Assert.IsType<TableColumn>(new Sample("SELECT (SELECT TOP (1) name FROM dbo.u AS u) AS c FROM dbo.t AS t;").Resolve("name"));

        Assert.Equal("u", column.Source.Alias);
    }

    [Theory]
    [InlineData("SELECT (SELECT TOP (1) name FROM #callers AS c) AS n FROM dbo.t AS t;", "c")]
    [InlineData("SELECT (SELECT TOP (1) name FROM (SELECT * FROM dbo.u AS u) AS d) AS n FROM dbo.t AS t;", "d")]
    [InlineData("SELECT (SELECT TOP (1) name FROM dbo.nope AS x) AS n FROM dbo.t AS t;", "x")]
    public void A_column_a_source_of_unknown_columns_may_have_is_not_looked_for_outside(string statement, string inner)
    {
        // The outer t has name, but the inner source may have it too (a caller's #t, a SELECT * not expanded, a table not in
        // the project), and SQL Server would read the inner one: so it is the inner source's, whatever that holds.
        var source = new Sample(statement).Resolve("name") switch
        {
            UnlistedColumn unlisted => unlisted.Source,
            UnboundSource unbound => unbound.Source,
            var other => throw new InvalidOperationException($"name resolved to {other}."),
        };

        Assert.Equal(inner, source.Alias);
    }

    [Fact]
    public void A_column_two_sources_of_one_scope_have_is_ambiguous()
    {
        var resolution = Assert.IsType<AmbiguousColumn>(new Sample("SELECT name FROM dbo.t AS t JOIN dbo.u AS u ON u.t_id = t.id;").Resolve("name"));

        Assert.Equal(BindingProblemKind.UnresolvedColumn, resolution.Problem.Kind);
        Assert.Contains("ambiguous", resolution.Problem.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT t.nope FROM dbo.t AS t;", "t.nope", "The table dbo.t has no column nope.")]
    [InlineData("SELECT nope FROM dbo.t AS t;", "nope", "No table in scope has the column nope.")]
    [InlineData("WITH c AS (SELECT t.id FROM dbo.t AS t) SELECT c.nope FROM c;", "c.nope", "c has no column nope.")]
    public void A_column_no_source_has_is_missing(string statement, string reference, string detail)
    {
        var resolution = Assert.IsType<MissingColumn>(new Sample(statement).Resolve(reference));

        Assert.Equal(BindingProblemKind.UnresolvedColumn, resolution.Problem.Kind);
        Assert.Equal(detail, resolution.Problem.Detail);
    }

    [Fact]
    public void A_column_reference_of_three_parts_is_not_modelled()
    {
        var resolution = Assert.IsType<UnmodelledReference>(new Sample("SELECT dbo.t.id FROM dbo.t;").Resolve("dbo.t.id"));

        Assert.Equal(BindingProblemKind.UnsupportedReference, resolution.Problem.Kind);
    }

    [Fact]
    public void A_column_of_a_source_that_cannot_be_bound_has_the_sources_problem_where_the_column_is()
    {
        var body = new Sample("SELECT s.id\nFROM elsewhere.dbo.t AS s;");
        var resolution = Assert.IsType<UnboundSource>(body.Resolve("s.id"));

        Assert.Equal(BindingProblemKind.UnsupportedSource, resolution.Problem.Kind);
        Assert.Equal(2, resolution.Problem.At.Line);
        Assert.Equal(8, resolution.Problem.At.Column);
    }

    // ---- what a name in FROM means ------------------------------------------------------------------------------------

    [Fact]
    public void A_cte_hides_a_table_of_its_name_until_its_statement_ends()
    {
        var body = new Sample("WITH shelf (shelf_id, label) AS (SELECT t.id, t.name FROM dbo.t AS t) SELECT s.label FROM shelf AS s;");

        var cte = Assert.IsType<CommonTable>(Source<RelationTarget>(body.Query()).Relation);
        Assert.Equal(["shelf_id", "label"], cte.Columns);
        Assert.False(cte.IsRecursive);

        // Outside the statement's WITH, the name is the table again.
        var select = (SelectStatement)body.Statement;
        Assert.Equal("dbo.shelf", Source<TableTarget>(body.Binder.Bind(select.QueryExpression, null, NameEnvironment.Empty)).Table.Name.ToString());
    }

    [Fact]
    public void A_cte_that_reads_its_own_name_is_recursive_and_named_by_its_anchor()
    {
        var body = new Sample("""
            WITH chain AS (
                SELECT t.id, 0 AS depth FROM dbo.t AS t
                UNION ALL
                SELECT c.id, c.depth + 1 FROM chain AS c WHERE c.depth < 3
            )
            SELECT c.id, c.depth FROM chain AS c;
            """);
        var with = body.Binder.BindWith(((SelectStatement)body.Statement).WithCtesAndXmlNamespaces, NameEnvironment.Empty);
        var cte = Assert.Single(with.Ctes);

        Assert.True(cte.Symbol.IsRecursive);
        Assert.Equal(["id", "depth"], cte.Symbol.Columns);
        var recursive = (BoundSpecification)((BoundSetOperation)cte.Query).Second;
        Assert.Same(cte.Symbol, Assert.IsType<RelationTarget>(Assert.Single(recursive.Scope.Sources).Target).Relation);
    }

    [Fact]
    public void A_derived_table_is_named_by_its_column_list_or_its_query_s_columns()
    {
        var listed = new Sample("SELECT d.a FROM (SELECT t.id, t.name FROM dbo.t AS t) AS d (a, b);").Query();
        var named = new Sample("SELECT d.id FROM (SELECT t.id, t.name AS label FROM dbo.t AS t) AS d;").Query();
        var unnamed = new Sample("SELECT d.id FROM (SELECT t.id, t.id + 1 FROM dbo.t AS t) AS d;").Query();

        Assert.Equal(["a", "b"], Source<RelationTarget>(listed).Relation.Columns);
        Assert.Equal(["id", "label"], Source<RelationTarget>(named).Relation.Columns);
        Assert.Null(Source<RelationTarget>(unnamed).Relation.Columns);
    }

    [Fact]
    public void A_derived_table_reads_the_enclosing_query_but_an_applied_one_reads_its_left_input_too()
    {
        var query = new Sample("""
            SELECT a.n, d.n
            FROM dbo.t AS t
            CROSS APPLY (SELECT COUNT(*) AS n FROM dbo.u AS u WHERE u.t_id = t.id) AS a
            CROSS JOIN (SELECT COUNT(*) AS n FROM dbo.u AS u) AS d;
            """).Query();
        var sources = ((BoundSpecification)query).Scope.Sources;
        var applied = (BoundSpecification)((DerivedTable)((RelationTarget)sources[1].Target).Relation).Query;
        var derived = (BoundSpecification)((DerivedTable)((RelationTarget)sources[2].Target).Relation).Query;

        Assert.Same(sources[0], Assert.Single(applied.Scope.Parent!.Sources));
        Assert.Null(derived.Scope.Parent);
    }

    [Fact]
    public void A_set_operation_is_named_by_its_first_query()
    {
        var query = new Sample("SELECT t.name AS who FROM dbo.t AS t UNION ALL SELECT u.name AS other FROM dbo.u AS u;").Query();

        Assert.Equal(["who"], query.Names);
    }

    [Fact]
    public void A_star_is_expanded_only_where_the_names_say_so()
    {
        var body = new Sample("SELECT * FROM dbo.t AS t;");

        Assert.Null(body.Query().Names);
        Assert.Equal(["id", "name", "code"], body.Query(NameEnvironment.View).Names);
    }

    [Fact]
    public void A_temporary_table_has_the_columns_the_walk_gives_it_and_otherwise_any()
    {
        var body = new Sample("SELECT x.a FROM #x AS x;");
        var known = NameEnvironment.Empty.WithTemporaryColumns([KeyValuePair.Create("#x", (IReadOnlyList<string>)["a", "b"])]);

        Assert.Equal(0, Assert.IsType<RelationColumn>(body.Resolve("x.a", known)).Ordinal);
        Assert.IsType<TemporaryTable>(Assert.IsType<UnlistedColumn>(body.Resolve("x.a")).Relation);
    }

    // ---- once per set of names ----------------------------------------------------------------------------------------

    [Fact]
    public void A_query_read_again_with_the_same_names_is_bound_once()
    {
        var body = new Sample("SELECT x.a FROM #x AS x;");
        static NameEnvironment With(params string[] columns) =>
            NameEnvironment.Empty.WithTemporaryColumns([KeyValuePair.Create("#x", (IReadOnlyList<string>)columns)]);

        Assert.Same(body.Query(With("a")), body.Query(With("a")));
        Assert.NotSame(body.Query(With("a")), body.Query(With("a", "b")));
    }

    // ---- variables and declarations -----------------------------------------------------------------------------------

    /// <summary>A procedure's declarations, as binding reads them.</summary>
    private static (BodyDeclarations Declarations, StatementList Body) Declarations(string parameters, string body)
    {
        using var dir = new TempDirectory();
        dir.File("p.sql", $"CREATE PROCEDURE dbo.p {parameters}\nAS\n{body}");
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.Empty(read.Diagnostics);
        var procedure = read.Model.FindProcedure(new ObjectName("dbo", "p"))!;
        var statements = ((SqlBody)procedure.Body).Statements;
        return (new Bindings(read.Model).For(procedure.Span).Declarations(procedure.Parameters, statements), statements);
    }

    [Fact]
    public void A_variable_is_a_parameter_a_declared_variable_or_undeclared()
    {
        // Declared in a branch not taken, @late still exists: declarations are compiled, not run.
        var (declarations, _) = Declarations("@id int", "IF 1 = 0 DECLARE @late nvarchar(10);\nSELECT @id AS a, @late AS b, @none AS c;");
        var names = NameEnvironment.Empty.WithDeclarations(declarations);

        Assert.IsType<ParameterVariable>(Binder.Resolve(new VariableReference { Name = "@ID" }, names));
        Assert.Equal("nvarchar(10)", Assert.IsType<LocalVariable>(Binder.Resolve(new VariableReference { Name = "@late" }, names)).Type!.ToString());
        Assert.IsType<UndeclaredVariable>(Binder.Resolve(new VariableReference { Name = "@none" }, names));
    }

    [Fact]
    public void A_declare_of_a_parameters_name_declares_nothing_new()
    {
        var (declarations, body) = Declarations("@id int", "DECLARE @id int = 1, @n int = 2;");
        var elements = ((DeclareVariableStatement)body.Statements[0]).Declarations;

        Assert.IsType<ParameterVariable>(declarations.Declared(elements[0]));
        Assert.Equal("@n", Assert.IsType<LocalVariable>(declarations.Declared(elements[1])).Name);
        Assert.Equal(["@n"], declarations.Variables.Select(v => v.Name));
    }

    [Fact]
    public void A_table_variable_and_a_cursor_are_declared_for_the_whole_body()
    {
        var (declarations, _) = Declarations("", "DECLARE @t TABLE (a int NOT NULL);\nDECLARE c CURSOR FOR SELECT 1 AS x;\nFETCH NEXT FROM c;");

        Assert.Equal("@t", Assert.Single(declarations.TableVariables).Name);
        Assert.Single(declarations.Cursors(new CursorId { Name = new IdentifierOrValueExpression { Identifier = new Identifier { Value = "C" } } }));
    }

    [Theory]
    [InlineData("#t", "#t")]
    [InlineData("##t", null)]
    [InlineData("dbo.#t", null)]
    [InlineData("t", null)]
    public void A_local_temporary_table_is_a_one_part_name_of_one_hash(string written, string? temporary)
    {
        var name = new SchemaObjectName();
        foreach (var part in written.Split('.'))
            name.Identifiers.Add(new Identifier { Value = part });

        Assert.Equal(temporary, Binder.Temporary(name));
    }
}
