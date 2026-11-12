namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// One of SQL Server's built-in functions, in one form (<c>COUNT</c> as an aggregate and over a window are two): its name and
/// its stable code. The catalogue is <see cref="BuiltInFunctions"/>, and no other can be made.
/// </summary>
/// <remarks>
/// <b>The code</b> is the function's form and name, <c>window.count</c>, which SQL Server doesn't change, so that explaining a
/// value can cite it and a link to it lasts. A code is never changed or reused: <c>BuiltInFunctionsTests</c> holds the list as a
/// snapshot, so a change to one shows, and checks no two entries share one.
/// </remarks>
public sealed class BuiltInFunction
{
    internal BuiltInFunction(string name, FunctionForm form)
    {
        Name = name;
        Form = form;
        Code = form switch
        {
            FunctionForm.Scalar => "scalar.",
            FunctionForm.Aggregate => "aggregate.",
            FunctionForm.Window => "window.",
            _ => "rowset.",
        } + name;
    }

    /// <summary>Its name in lower case: <c>datediff</c>.</summary>
    public string Name { get; }

    /// <summary>How it is called.</summary>
    public FunctionForm Form { get; }

    /// <summary>Its stable code, <c>scalar.datediff</c>.</summary>
    public string Code { get; }

    /// <summary>Its code.</summary>
    public override string ToString() => Code;
}
