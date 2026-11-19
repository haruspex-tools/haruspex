using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The type SQL Server gives an undeclared parameter from one use of it, as <c>sp_describe_undeclared_parameters</c> answers: by
/// how it is used and the type of what it is used with. Each rule is observed (<c>QueryParameterObservationTests</c>), and a type
/// a rule doesn't name gives nothing, so that a use the server would type otherwise, or refuse, decides nothing here.
/// </summary>
internal static class ParameterRules
{
    /// <summary>How a parameter is compared with an operand.</summary>
    internal enum Comparison
    {
        /// <summary><c>=</c>, <c>&lt;&gt;</c>, <c>!=</c>, or a value of an <c>IN</c> list: the operand's type exactly.</summary>
        Equality,

        /// <summary><c>&lt;</c>, <c>&gt;</c>, their kin and <c>BETWEEN</c>: a bound needn't fit the operand, so the type is widened.</summary>
        Ordering,

        /// <summary><c>LIKE</c>, either side: a string of the operand's kind, widened.</summary>
        Like,
    }

    /// <summary>A count of rows, of TOP, OFFSET or FETCH: <c>bigint</c> (observed).</summary>
    public static ScalarType RowCount { get; } = ScalarType.Of(BuiltInType.BigInt);

    /// <summary>A percentage of rows, of TOP … PERCENT: <c>float</c> (observed).</summary>
    public static ScalarType Percent { get; } = ScalarType.Of(BuiltInType.Float);

    /// <summary>The widest decimal the server gives where a decimal's digits are not the operand's: <c>decimal(38,19)</c> (observed).</summary>
    private static readonly ScalarType WideDecimal = new(BuiltInType.Decimal, Precision: 38, Scale: 19);

    /// <summary>Whether a comparison tests equality, which takes the operand's type exactly.</summary>
    public static bool IsEquality(BooleanComparisonType type) =>
        type is BooleanComparisonType.Equals or BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation;

    /// <summary>
    /// The type a parameter compared with an operand of <paramref name="operand"/>'s type takes; null where the server's rule isn't
    /// modelled.
    /// </summary>
    /// <remarks>
    /// Observed: equality takes the operand's type, facets and all (<c>nvarchar(80)</c>, <c>decimal(10,2)</c>, <c>char(3)</c>,
    /// <c>datetime2(0)</c>). Ordering keeps a type of fixed size, but widens a decimal to <c>decimal(38,19)</c>, a string to the longest
    /// of its kind that isn't max (<c>varchar(8000)</c>, <c>nvarchar(4000)</c>, a fixed one to the varying one) while max stays max, and a time, <c>datetime2</c> or <c>datetimeoffset</c> to 7 places. LIKE gives
    /// <c>varchar(8000)</c> or <c>nvarchar(4000)</c> by the operand's kind, max or not. A <c>uniqueidentifier</c> under ordering, and
    /// anything else not named here, gives nothing. A binary value's rules are the string's: equality takes its type, a
    /// <c>rowversion</c>'s too, and ordering <c>varbinary(8000)</c>, max staying max (observed:
    /// A_parameter_compared_with_a_binary_value_takes_its_type_under_equality_and_varbinary_8000_under_ordering,
    /// A_parameter_compared_with_a_binary_column_of_any_kind_takes_its_type_under_equality_and_varbinary_under_ordering); LIKE is not
    /// modelled.
    /// </remarks>
    public static ScalarType? Compared(ScalarType operand, Comparison how)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return how switch
        {
            Comparison.Equality => operand,
            Comparison.Ordering => operand.Family switch
            {
                TypeFamily.Bit or TypeFamily.Integer or TypeFamily.Money or TypeFamily.Approximate => operand,
                TypeFamily.Decimal => WideDecimal,
                TypeFamily.String => Varying(operand, max: operand.IsMax),
                TypeFamily.Binary => ScalarType.ByteString(varying: true, operand.IsMax ? -1 : BuiltInType.MostBytes),
                TypeFamily.DateAndTime => operand.Type == BuiltInType.Date || operand.Type == BuiltInType.DateTime || operand.Type == BuiltInType.SmallDateTime
                    ? operand
                    : operand with { Precision = 7 },
                _ => null,
            },
            Comparison.Like => operand.IsString ? Varying(operand, max: false) : null,
            _ => null,
        };
    }

    /// <summary>
    /// The type a parameter in arithmetic with an operand of <paramref name="operand"/>'s type takes; null where the server's rule
    /// isn't modelled.
    /// </summary>
    /// <remarks>
    /// Observed: an integer, an approximate number or money takes the operand's type, under any of <c>+ - * / %</c> and an integer
    /// under <c>&amp; | ^</c> too; a decimal takes <c>decimal(38,19)</c>; under <c>+</c> alone, a string takes <c>varchar(8000)</c> or
    /// <c>nvarchar(4000)</c> by its kind, max or not, and a <c>datetime</c> or
    /// <c>smalldatetime</c> its own type. Not <c>bit</c>, under which the server gives <c>datetime</c> for <c>+</c>, nor the other
    /// date and time types, which it refuses.
    /// </remarks>
    public static ScalarType? Arithmetic(ScalarType operand, BinaryExpressionType how)
    {
        ArgumentNullException.ThrowIfNull(operand);
        var arithmetic = how is BinaryExpressionType.Add or BinaryExpressionType.Subtract or BinaryExpressionType.Multiply or BinaryExpressionType.Divide or BinaryExpressionType.Modulo;
        var bitwise = how is BinaryExpressionType.BitwiseAnd or BinaryExpressionType.BitwiseOr or BinaryExpressionType.BitwiseXor;
        return operand.Family switch
        {
            TypeFamily.Integer when arithmetic || bitwise => operand,
            TypeFamily.Money or TypeFamily.Approximate when arithmetic => operand,
            TypeFamily.Decimal when arithmetic => WideDecimal,
            TypeFamily.String when how == BinaryExpressionType.Add => Varying(operand, max: false),
            TypeFamily.DateAndTime when how == BinaryExpressionType.Add && (operand.Type == BuiltInType.DateTime || operand.Type == BuiltInType.SmallDateTime) => operand,
            _ => null,
        };
    }

    /// <summary>The varying string of a string's kind: max, or the longest that isn't.</summary>
    private static ScalarType Varying(ScalarType operand, bool max)
    {
        var type = operand.IsNational ? BuiltInType.NVarChar : BuiltInType.VarChar;
        return new ScalarType(type, max ? -1 : type.MostLength);
    }
}
