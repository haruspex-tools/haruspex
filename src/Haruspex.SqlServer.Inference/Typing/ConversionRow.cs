using Haruspex.SqlServer.Semantics;

namespace Haruspex.SqlServer.Inference.Typing;

/// <summary>
/// A row of the table of conversions (<see cref="Conversions.Table"/>): a value of a type in <see cref="From"/> converted to a
/// type in <see cref="To"/> keeps every value, with no overflow, when <see cref="When"/> holds.
/// </summary>
/// <param name="To">The target types.</param>
/// <param name="From">The source types.</param>
/// <param name="When">The condition on their arguments.</param>
/// <param name="ObservedIn">The observation tests that establish the row, by name.</param>
internal sealed record ConversionRow(IReadOnlyList<BuiltInType> To, IReadOnlyList<BuiltInType> From, ConversionCondition When, IReadOnlyList<string> ObservedIn);
