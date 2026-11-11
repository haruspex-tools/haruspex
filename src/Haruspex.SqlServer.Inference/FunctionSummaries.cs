using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The return of every scalar function a body calls, worked out once per document. A function whose body returns JSON
/// built here has that JSON's shape; one that calls itself, directly or through others, gets a recursive named type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which bodies are read.</b> Only a return type that holds any JSON text unchanged: <c>nvarchar(max)</c> or <c>json</c>.
/// A shorter type cuts the text short (observed: <c>nvarchar(10)</c> returns <c>[{"id":1,"</c>), and <c>varchar</c> may
/// replace characters its code page lacks. The body is one of:
/// <c>RETURN expression</c>; or a straight run of <c>DECLARE @v</c>, <c>SET @v = …</c> and <c>SELECT @v = …</c> (with no other
/// clause: no FROM, WHERE, TOP, GROUP BY, HAVING, ORDER BY or OFFSET, any of which can make it assign nothing)
/// ending in <c>RETURN @v</c>, where the last value given to <c>@v</c> is the result and <c>@v</c> is <c>nvarchar(max)</c>
/// or <c>json</c>. The expression must be live JSON (a FOR JSON subquery, a constructor, <c>json_query</c>) or JSON text
/// whose content is known. Anything else (control flow, a compound assignment, an assignment to a parameter, a
/// statement of any other kind) gives the declared return type, nullable (<c>function-result</c>): a scalar function may
/// return NULL from any body.
/// </para>
/// <para>
/// <b>Parameters</b> inside the body are nullable (<c>parameter</c>): the function's shape is the same for every call, so
/// it must hold whatever a caller passes. With <c>RETURNS NULL ON NULL INPUT</c> the body never runs with a null argument
/// (the call is NULL instead, observed), so there they are non-null, and a call with an argument that may be null is
/// nullable (<c>null-input</c>).
/// </para>
/// <para>
/// <b>Recursion</b> is solved per strongly connected component of the call graph (Tarjan), components below first. In a
/// component whose functions call each other or themselves, each function's result starts as no information
/// (<see cref="NeverShape"/>, nothing nullable), and every body is read again with the previous step's results until no
/// result changes: the least fixed point, which describes every call that returns, by induction on its depth. A call
/// of a function in the component gives a reference to its node rather than the node itself: its object, or the object
/// items of its array (what FOR JSON PATH and <c>json_object</c> build), so that each step's shape is finite and the
/// steps can settle. Any other result (a tuple, a record) is not named and so grows at each step. Nullability only
/// grows (reasons are added, never removed), and a result goes from no information to a shape once, so a component of n
/// functions settles within a few steps per function; at most <see cref="StepsPerFunction"/> per function, plus
/// <see cref="ExtraSteps"/>, are taken. A component that has not settled by then, and a function whose result is still no
/// information when it settles (every return calls itself again, so it never returns), is unknown
/// (<c>unresolved-recursion</c>). The type does not say how deep a tree goes: SQL Server stops at 32 nested calls with
/// error 217 (observed), which is an error, not a value.
/// </para>
/// </remarks>
internal sealed class FunctionSummaries
{
    /// <summary>Steps allowed per function of a recursive component, before giving up.</summary>
    public const int StepsPerFunction = 4;

    /// <summary>Steps allowed per recursive component on top of <see cref="StepsPerFunction"/>.</summary>
    public const int ExtraSteps = 4;

    private readonly DatabaseModel model;
    private readonly Dictionary<ObjectName, FunctionReturn> done = [];
    private readonly Dictionary<ObjectName, ObjectShape> nodes = [];
    private readonly Dictionary<ObjectName, int> component = [];
    private readonly List<List<ObjectName>> components = [];
    private readonly HashSet<int> recursiveComponents = [];

    /// <summary>The results assumed for the component being solved, at the current step.</summary>
    private Dictionary<ObjectName, FunctionReturn>? assumed;

    /// <summary>The views a body may read, once they are known (<see cref="ViewSummaries"/> sets it); null leaves them unread.</summary>
    public ViewSummaries? Views { get; set; }

    /// <summary>Whether every body is read uncommitted, so that no key of what it reads holds.</summary>
    private readonly bool uncommitted;

    /// <summary>
    /// The same functions read uncommitted, for calls in a statement that reads uncommitted (review round 1 of slice 6f). A
    /// recursive function is not read there: it gives its declared type only, so that its named node (<see cref="Nodes"/>,
    /// which the IR's type table holds once) comes from the committed reading alone. That only claims less.
    /// </summary>
    public FunctionSummaries Uncommitted => uncommitted ? this : field ??= new FunctionSummaries(model, uncommitted: true) { Views = Views };

    public FunctionSummaries(DatabaseModel model, bool uncommitted = false)
    {
        this.uncommitted = uncommitted;
        this.model = model;
        FindComponents();
    }

    /// <summary>The object each recursive function's node stands for, settled at the fixed point.</summary>
    public IReadOnlyDictionary<ObjectName, ObjectShape> Nodes => nodes;

    /// <summary>The function a call names, when it is a scalar function of the model whose definition can be trusted.</summary>
    /// <returns>The function's return, or an unknown saying why there is none.</returns>
    public OneOf Find(ObjectName name, SourceLocation at)
    {
        var function = model.FindFunction(name);
        if (function is null)
            return new OneOf(null, new UnknownShape(IrUnknownReasons.UnsupportedExpression, $"{name} is not a function of the project.", at));
        if (function.Redefinition is { } redefinition)
            return new OneOf(null, new UnknownShape(IrUnknownReasons.Redefined, redefinition.Message, redefinition.At));
        if (function.Body.ReturnType is not ScalarFunctionReturnType)
            return new OneOf(null, new UnknownShape(IrUnknownReasons.UnsupportedExpression, $"{name} is not a scalar function.", at));
        return new OneOf(Return(function), null);
    }

    /// <summary>A function's return, or the reason it has none.</summary>
    internal readonly record struct OneOf(FunctionReturn? Return, UnknownShape? Unknown);

    private FunctionReturn Return(UserFunction function)
    {
        if (assumed is not null && assumed.TryGetValue(function.Name, out var current))
            return current;
        if (done.TryGetValue(function.Name, out var result))
            return result;

        Solve(component[function.Name]);
        return done[function.Name];
    }

    // ---- the fixed point ------------------------------------------------------------------------------------

    private void Solve(int index)
    {
        var members = components[index];
        if (!recursiveComponents.Contains(index))
        {
            var only = model.FindFunction(members[0])!;
            done[only.Name] = Read(only);
            return;
        }

        if (uncommitted)
        {
            foreach (var member in members.Select(m => model.FindFunction(m)!))
                done[member.Name] = Read(member, header: true) with { Content = null, NullableBecause = [new IrNullReason(IrNullRules.FunctionResult, member.At.ToString())] };
            return;
        }

        var outer = assumed;
        var functions = members.Select(m => model.FindFunction(m)!).ToList();
        var current = functions.ToDictionary(f => f.Name, Start);
        var steps = (StepsPerFunction * members.Count) + ExtraSteps;
        var settled = false;
        try
        {
            for (var step = 0; step < steps && !settled; step++)
            {
                assumed = current.ToDictionary(p => p.Key, p => Abstract(p.Key, p.Value));
                var next = functions.ToDictionary(f => f.Name, f => Read(f));
                settled = functions.All(f => ShapeText.Of(next[f.Name]) == ShapeText.Of(current[f.Name]));
                current = next;
            }
        }
        finally
        {
            assumed = outer;
        }

        // A node that no finite value can have (every cycle through it goes through keys that are always present and
        // never null, such as json_object('c': json_query(f(@x)))) means the call never returns: SQL Server raises error
        // 217 at 32 levels. The whole component is withdrawn rather than typed with a type that has no value.
        var finite = settled ? Finite(current) : [];
        var endless = settled && current.Any(p => Node(p.Value.Content) is not null && !finite.Contains(p.Key));

        foreach (var function in functions)
        {
            var result = current[function.Name];
            var at = function.At;
            if (endless && result.Content is not null)
            {
                result = Unresolved(result, new UnknownShape(
                    IrUnknownReasons.UnresolvedRecursion,
                    $"{function.Name} calls itself through keys that are always present and never null, so no call ends: it never returns a value.",
                    at));
            }
            else if (!settled)
            {
                result = Unresolved(result, new UnknownShape(
                    IrUnknownReasons.UnresolvedRecursion,
                    $"{function.Name} calls itself, and its JSON's shape reached no fixed point in {steps} steps.",
                    at));
            }
            else if (result.Content is NeverShape)
            {
                result = Unresolved(result, new UnknownShape(
                    IrUnknownReasons.UnresolvedRecursion,
                    $"Every return of {function.Name} calls itself again, so it never returns a value.",
                    at));
            }
            else if (Node(result.Content) is { } node)
            {
                nodes[function.Name] = node;
            }

            done[function.Name] = Abstract(function.Name, result);
        }
    }

    /// <summary>
    /// The functions of a settled component whose node has a finite value: a least fixed point, starting from none. A
    /// node has one when each of its keys can be left out or null, or has a type with a finite value; an array without
    /// a fewest number of items always has one (<c>[]</c>), a scalar always has one, and JSON text has one when its content
    /// does (or its content is unknown).
    /// </summary>
    private static HashSet<ObjectName> Finite(Dictionary<ObjectName, FunctionReturn> results)
    {
        var finite = new HashSet<ObjectName>();
        bool Has(Shape shape) => shape switch
        {
            RecursiveShape recursive => finite.Contains(recursive.Function),
            NeverShape => false,
            ObjectShape obj => obj.Fields.All(f => f.NullableBecause.Count > 0 || Has(f.Type)),
            ArrayShape array => array.MinItems is null or 0 || Has(array.Items),
            TupleShape tuple => tuple.Items.All(e => e.NullableBecause.Count > 0 || Has(e.Type)),
            UnionShape union => union.Variants.Any(Has),

            // Text built here holds its content, so it has a finite value only when the content does (a plain call of
            // an endless function is endless too); text whose content is unknown claims only "JSON".
            JsonTextShape { Content: { } content } => Has(content),
            _ => true,
        };

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (name, result) in results)
            {
                if (!finite.Contains(name) && Node(result.Content) is { } node && Has(node))
                    changed = finite.Add(name) || changed;
            }
        }

        return finite;
    }

    /// <summary>No information: the result before any step.</summary>
    private FunctionReturn Start(UserFunction function)
    {
        var start = Read(function, header: true);
        return start.Content is null ? start : start with { Content = new NeverShape(function.At), NullableBecause = [] };
    }

    private static FunctionReturn Unresolved(FunctionReturn result, UnknownShape why) =>
        result with { Content = why, NullableBecause = [new IrNullReason(IrNullRules.Unknown, why.At.ToString())] };

    /// <summary>
    /// What a call of a function in the component gives: a reference to its node in place of the node (its object, or the
    /// objects of its array). A result with no node is left as it is.
    /// </summary>
    private static FunctionReturn Abstract(ObjectName name, FunctionReturn result) => result.Content switch
    {
        ObjectShape => result with { Content = new RecursiveShape(name) },
        ArrayShape { Items: ObjectShape } array => result with { Content = array with { Items = new RecursiveShape(name) } },
        _ => result,
    };

    private static ObjectShape? Node(Shape? content) => content switch
    {
        ObjectShape obj => obj,
        ArrayShape { Items: ObjectShape obj } => obj,
        _ => null,
    };

    // ---- one function's body ---------------------------------------------------------------------------------

    /// <summary>Reads a body. Inside a recursive component, the calls it makes see the previous step's results.</summary>
    /// <param name="header">Read only the header: whether the body is a form that can return JSON at all.</param>
    private FunctionReturn Read(UserFunction function, bool header = false)
    {
        var body = function.Body;
        var at = function.At;
        var returnType = SqlType.From(((ScalarFunctionReturnType)body.ReturnType).DataType);
        var resolved = SchemaRules.ResolveType(model, returnType)?.BuiltIn;
        var returnsJson = resolved?.Name == "json";
        var nullOnNull = body.Options?.Any(o => o.OptionKind == FunctionOptionKind.ReturnsNullOnNullInput) == true;

        // The declared type: a json return is live JSON whose shape is in the data; anything else as a variable is typed.
        var declared = returnsJson
            ? new UnknownShape(IrUnknownReasons.JsonShapeUnknown, $"{function.Name} returns json that is not built in a form read here.", at)
            : SqlTypes.ToShape(model, returnType, at);
        var declaredOnly = new FunctionReturn(declared, returnsJson, null, [new IrNullReason(IrNullRules.FunctionResult, at.ToString())], nullOnNull);

        if (body.MethodSpecifier is not null || body.StatementList is null || !HoldsAnyJson(resolved))
            return declaredOnly;
        if (Returned(body) is not { } expression)
            return declaredOnly;
        if (header)
            return declaredOnly with { Content = new NeverShape(at) };

        var parameters = body.Parameters.ToDictionary(
            p => p.VariableName.Value,
            p => Parameter(p, nullOnNull, function.File),
            StringComparer.OrdinalIgnoreCase);
        var value = new ProcedureAnalyzer(model, function.File, this, parameters, ansiNullsOff: function.AnsiNullsOff, views: Views, uncommittedReads: uncommitted).Return(expression, body.Parameters);

        var content = value switch
        {
            { IsLiveJson: true } => value.Shape,
            { Shape: JsonTextShape { Content: { } text } } => text,
            _ => null,
        };
        return content is null ? declaredOnly : new FunctionReturn(declared, returnsJson, content, value.NullableBecause, nullOnNull);
    }

    /// <summary>
    /// A parameter inside the body: its declared type, with the type's facts (the argument has been converted to it: a
    /// string cut short, a decimal rounded, observed), nullable unless the body never runs with a null argument.
    /// </summary>
    private Value Parameter(ProcedureParameter parameter, bool nullOnNull, string file)
    {
        var at = new SourceLocation(file, parameter.StartLine, parameter.StartColumn);
        IReadOnlyList<IrNullReason> because = nullOnNull ? [] : [new IrNullReason(IrNullRules.Parameter, parameter.VariableName.Value)];
        var type = SqlType.From(parameter.DataType);

        // A json value nests as JSON (observed for variables and columns), and its shape is in the data.
        return SchemaRules.ResolveType(model, type)?.BuiltIn.Name == "json"
            ? new Value(new UnknownShape(IrUnknownReasons.JsonShapeUnknown, $"{parameter.VariableName.Value} is a json parameter: its shape is in the data.", at), because, IsLiveJson: true)
            : new Value(SqlTypes.ToConvertedShape(model, type, at), because);
    }

    /// <summary><c>nvarchar(max)</c> and <c>json</c> hold any JSON text unchanged.</summary>
    private static bool HoldsAnyJson(SqlType? type) =>
        type is not null
        && (type.Name == "json" || (type.Name == "nvarchar" && type.Arguments is [var length] && length.Equals("max", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The expression the body returns, when the body is <c>RETURN expression</c> or a straight run of declarations and
    /// assignments ending in <c>RETURN @v</c>; null for any other body.
    /// </summary>
    private ScalarExpression? Returned(FunctionStatementBody body)
    {
        var statements = body.StatementList.Statements.ToList();
        while (statements is [BeginEndBlockStatement block])
            statements = [.. block.StatementList.Statements];
        if (statements is not [.. var before, ReturnStatement { Expression: { } returned }])
            return null;

        var parameters = body.Parameters.Select(p => p.VariableName.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var variables = new Dictionary<string, (bool HoldsJson, ScalarExpression? Value)>(StringComparer.OrdinalIgnoreCase);

        bool Assign(string name, ScalarExpression value)
        {
            if (parameters.Contains(name) || !variables.TryGetValue(name, out var variable))
                return false;
            variables[name] = variable with { Value = value };
            return true;
        }

        foreach (var statement in before)
        {
            switch (statement)
            {
                case DeclareVariableStatement declare:
                    foreach (var variable in declare.Declarations)
                    {
                        // A table variable or a cursor is not a value; a variable declared twice fails to compile.
                        if (variable.DataType is null || variables.ContainsKey(variable.VariableName.Value))
                            return null;
                        variables[variable.VariableName.Value] = (HoldsAnyJson(SchemaRules.ResolveType(model, SqlType.From(variable.DataType))?.BuiltIn), variable.Value);
                    }

                    break;
                case SetVariableStatement { AssignmentKind: AssignmentKind.Equals, FunctionCallExists: false, Identifier: null, Expression: { } value, Variable: { } target } when Assign(target.Name, value):
                    break;
                // Only a SELECT that assigns exactly once: TOP (0), HAVING, OFFSET … FETCH or a WHERE can make it assign
                // nothing, leaving the variable as it was (observed: TOP (0) and HAVING 1 = 0 return NULL).
                case SelectStatement
                {
                    Into: null,
                    WithCtesAndXmlNamespaces: null,
                    ComputeClauses.Count: 0,
                    QueryExpression: QuerySpecification
                    {
                        FromClause: null, WhereClause: null, ForClause: null, TopRowFilter: null, GroupByClause: null,
                        HavingClause: null, OrderByClause: null, OffsetClause: null, UniqueRowFilter: UniqueRowFilter.NotSpecified,
                        SelectElements: [SelectSetVariable { AssignmentKind: AssignmentKind.Equals } set],
                    },
                }
                    when Assign(set.Variable.Name, set.Expression):
                    break;
                default:
                    return null;
            }
        }

        if (returned is not VariableReference reference)
            return returned;

        // RETURN @v of a variable never assigned returns NULL, which is not JSON; one too short has been cut.
        return variables.TryGetValue(reference.Name, out var returnedVariable) && returnedVariable is { HoldsJson: true, Value: { } last }
            ? last
            : null;
    }

    // ---- the call graph --------------------------------------------------------------------------------------

    /// <summary>Tarjan's algorithm over the scalar functions' calls, so each component is solved once, those it calls first.</summary>
    private void FindComponents()
    {
        var scalar = model.Functions.Where(f => f.Body.ReturnType is ScalarFunctionReturnType).ToList();
        var calls = scalar.ToDictionary(
            f => f.Name,
            f => Calls(f.Body).Where(c => model.FindFunction(c)?.Body.ReturnType is ScalarFunctionReturnType).Distinct().ToList());

        var index = 0;
        var indexes = new Dictionary<ObjectName, int>();
        var lowest = new Dictionary<ObjectName, int>();
        var stack = new Stack<ObjectName>();
        var onStack = new HashSet<ObjectName>();

        void Visit(ObjectName name)
        {
            indexes[name] = lowest[name] = index++;
            stack.Push(name);
            onStack.Add(name);
            foreach (var callee in calls[name])
            {
                if (!indexes.TryGetValue(callee, out var calleeIndex))
                {
                    Visit(callee);
                    lowest[name] = Math.Min(lowest[name], lowest[callee]);
                }
                else if (onStack.Contains(callee))
                {
                    lowest[name] = Math.Min(lowest[name], calleeIndex);
                }
            }

            if (lowest[name] != indexes[name])
                return;

            var members = new List<ObjectName>();
            ObjectName member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                members.Add(member);
                component[member] = components.Count;
            }
            while (!member.Equals(name));

            members.Sort((a, b) => StringComparer.Ordinal.Compare(a.ToString(), b.ToString()));
            if (members.Count > 1 || calls[name].Contains(name))
                recursiveComponents.Add(components.Count);
            components.Add(members);
        }

        foreach (var function in scalar)
        {
            if (!indexes.ContainsKey(function.Name))
                Visit(function.Name);
        }
    }

    /// <summary>The functions a body calls by a schema-qualified name, as a scalar function must be called.</summary>
    internal static IEnumerable<ObjectName> Calls(TSqlFragment body)
    {
        var visitor = new CallVisitor();
        body.Accept(visitor);
        return visitor.Calls;
    }

    /// <summary>The function a call names, when it is <c>schema.name(…)</c>.</summary>
    internal static ObjectName? Callee(FunctionCall call) =>
        call.CallTarget is MultiPartIdentifierCallTarget { MultiPartIdentifier.Identifiers: [var schema] }
            ? new ObjectName(schema.Value, call.FunctionName.Value)
            : null;

    private sealed class CallVisitor : TSqlFragmentVisitor
    {
        public List<ObjectName> Calls { get; } = [];

        public override void Visit(FunctionCall node)
        {
            if (Callee(node) is { } name)
                Calls.Add(name);
        }
    }
}
