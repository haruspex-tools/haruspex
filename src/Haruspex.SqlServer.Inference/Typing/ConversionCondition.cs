namespace Haruspex.SqlServer.Inference.Typing;

/// <summary>When a conversion the table lists (<see cref="ConversionRow"/>) keeps every value of its source type.</summary>
internal enum ConversionCondition
{
    /// <summary>Always: every value converts, or is cut short as a string is, which is not an overflow.</summary>
    Always,

    /// <summary>When the target string is as long as the source's longest value written as text.</summary>
    FitsAsText,

    /// <summary>When the target integer has at least the source's integer digits.</summary>
    WiderInteger,

    /// <summary>
    /// When the source decimal's integer digits are fewer than the target's: the target's top digit isn't free
    /// (<c>int</c>'s greatest value is 2147483647), so only one fewer always fits. The places are cut or rounded.
    /// </summary>
    IntegerPartFits,

    /// <summary>When the target decimal holds the source's integer digits, and one more if it rounds to fewer places.</summary>
    DecimalDigitsFit,
}
