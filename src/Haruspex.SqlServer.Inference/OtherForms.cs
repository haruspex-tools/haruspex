namespace Haruspex.SqlServer.Inference;

/// <summary>What a call of a built-in function gives with a number of arguments its row doesn't read (<see cref="FunctionTyping.Arity"/>).</summary>
internal enum OtherForms
{
    /// <summary>Unknown: this form of the function is not modelled.</summary>
    FormNotModelled,

    /// <summary>Unknown, as an expression not modelled at all.</summary>
    ExpressionNotModelled,

    /// <summary>Unknown, as an aggregate not modelled.</summary>
    AggregateNotModelled,

    /// <summary>Unknown, as a window function not modelled.</summary>
    WindowNotModelled,
}
