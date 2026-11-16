using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The bitwise operators, the shifts and ||, typed by Typing/Bitwise.cs. README.md, "Bitwise operators, shifts and the concatenation operator", says what each
// rests on.
internal sealed partial class Typer
{
    /// <summary>
    /// <c>a &amp; b</c>, <c>a | b</c>, <c>a ^ b</c>, <c>a &lt;&lt; n</c>, <c>a &gt;&gt; n</c> and <c>a || b</c>: of the type
    /// <see cref="Bitwise"/> gives, null when an operand is, whatever CONCAT_NULL_YIELDS_NULL says for <c>||</c> (observed:
    /// Bitwise_operators_shifts_and_concatenation_are_null_when_an_operand_is_and_never_overflow). A string beside an integer is
    /// converted to its type, which may overflow (<c>arithmetic-overflow</c>, as a CAST). Nothing else overflows: a shift past the
    /// width gives 0, and <c>||</c> cuts at 8000 bytes without an error. A pair the server refuses is unknown.
    /// </summary>
    private Value BitwiseBinary(BinaryExpression binary, ReadScope scope)
    {
        var symbol = binary.BinaryExpressionType switch
        {
            BinaryExpressionType.BitwiseAnd => "&",
            BinaryExpressionType.BitwiseOr => "|",
            BinaryExpressionType.BitwiseXor => "^",
            BinaryExpressionType.LeftShift => "<<",
            BinaryExpressionType.RightShift => ">>",
            BinaryExpressionType.Concat => "||",
            _ => null,
        };
        if (symbol is null)
            return NotModelled(binary, "this operator is not modelled yet.");

        var left = Expression(binary.FirstExpression, scope);
        var right = Expression(binary.SecondExpression, scope);
        if (left.Shape is UnknownShape)
            return left;
        if (right.Shape is UnknownShape)
            return right;
        if (OperandOf(left, binary.FirstExpression) is not { } l || OperandOf(right, binary.SecondExpression) is not { } r)
            return NotModelled(binary, "an operand of this type is not modelled yet.");

        var type = symbol switch
        {
            "<<" or ">>" => Bitwise.Shifted(l.Type, r.Type),
            "||" => Bitwise.Concatenated(l.Type, r.Type),
            _ => Bitwise.Logical(l.Type, r.Type),
        };
        if (type is null)
            return NotModelled(binary, $"SQL Server refuses {l.Type.Text} {symbol} {r.Type.Text}.");

        var because = NullReasons.Join(left.NullableBecause, right.NullableBecause);
        if (symbol is "&" or "|" or "^")
        {
            because = NullReasons.Join(because, StringConverted(left, binary.FirstExpression, l, type, binary));
            because = NullReasons.Join(because, StringConverted(right, binary.SecondExpression, r, type, binary));
        }

        return Typed(type, because, binary);
    }

    /// <summary>What converting a string operand to an integer's type adds: an overflow, unless every value fits.</summary>
    private EquatableArray<IrNullReason> StringConverted(Value value, ScalarExpression expression, Operand operand, ScalarType to, TSqlFragment at) =>
        operand.Type.IsString ? ConversionReasons(summaries.Session, value, expression, to, Conversions.Converts(operand, to), At(at)) : [];

    /// <summary><c>~a</c>: a's type, an integer or a bit, null when a is; any other type is refused (8117), so unknown.</summary>
    private Value BitwiseNot(UnaryExpression unary, ReadScope scope)
    {
        var operand = Expression(unary.Expression, scope);
        if (operand.Shape is UnknownShape)
            return operand;
        if (TypeOf(operand) is not { } type)
            return NotModelled(unary, "an operand of this type is not modelled yet.");
        return Bitwise.Not(type) is { } not
            ? Typed(not, operand.NullableBecause, unary)
            : NotModelled(unary, $"SQL Server refuses ~ of {type.Text}.");
    }
}
