using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// How a window function is typed, as data: what it gives, and the reasons beyond its argument's that it may be NULL. A row of
/// the function table (<c>Typer.FunctionTable.cs</c>) holds one for each window function; README.md in the project
/// ("Window functions") gives the observations.
/// </summary>
/// <param name="Result">What it gives.</param>
/// <param name="Type">For <see cref="WindowResult.Fixed"/>, the type.</param>
/// <param name="CountOverflow">Whether it may overflow int past 2,147,483,647 rows: <c>COUNT</c>.</param>
/// <param name="EmptyFrame">Whether it is NULL over a frame that may be empty.</param>
/// <param name="TooFewValues">Whether it is NULL over fewer than two values: the sample statistics.</param>
/// <param name="Average">For <see cref="WindowResult.Statistic"/>, whether it is <c>AVG</c>, typed by its argument.</param>
/// <param name="RefusesBit">Whether the server refuses it over a bit: <c>MIN</c> and <c>MAX</c>.</param>
/// <param name="AnotherRow">
/// Whether its value is read from one other row, where what a branch proved of the current row doesn't hold
/// (<see cref="Facts.InAnotherRow"/>): <c>FIRST_VALUE</c>, <c>LAST_VALUE</c>, and <c>LAG</c> and <c>LEAD</c> unless the offset is
/// 0. An aggregate or a percentile skips NULLs over a partition, or a frame, that holds the current row, so a branch's proof holds
/// for it.
/// </param>
internal sealed record WindowRule(
    WindowResult Result,
    BuiltInType? Type = null,
    bool CountOverflow = false,
    bool EmptyFrame = false,
    bool TooFewValues = false,
    bool Average = false,
    bool RefusesBit = false,
    bool AnotherRow = false);
