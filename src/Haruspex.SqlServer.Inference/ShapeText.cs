using System.Text;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A shape written as text, so that two shapes can be compared: the fixed point over recursive functions stops when a
/// step writes every function's result as the step before did. Records holding lists do not compare their items, which
/// is why the text is used rather than equality.
/// </summary>
internal static class ShapeText
{
    public static string Of(FunctionReturn result)
    {
        var text = new StringBuilder();
        text.Append(result.ReturnsJson).Append(result.NullOnNullInput).Append('|');
        Reasons(text, result.NullableBecause);
        text.Append('|');
        Write(text, result.Declared);
        text.Append('|');
        if (result.Content is { } content)
            Write(text, content);
        return text.ToString();
    }

    /// <summary>A shape as text, for comparing two shapes, such as the states of a fixed point.</summary>
    public static string Of(Shape shape)
    {
        var text = new StringBuilder();
        Write(text, shape);
        return text.ToString();
    }

    /// <summary>A value as text: its shape, its reasons to be null and whether it is live JSON.</summary>
    public static string Of(Value value)
    {
        var text = new StringBuilder();
        text.Append(value.IsLiveJson ? 'L' : 'T');
        Reasons(text, value.NullableBecause);
        Write(text, value.Shape);
        return text.ToString();
    }

    private static void Write(StringBuilder text, Shape shape)
    {
        switch (shape)
        {
            case ScalarShape scalar:
                text.Append(IrJsonWriter.WriteType(scalar.Type));
                break;
            case ArrayShape array:
                text.Append("array(");
                Write(text, array.Items);
                text.Append(',').Append(array.MinItems).Append(',').Append(array.MaxItems).Append(',');
                Reasons(text, array.ItemsNullableBecause ?? []);
                text.Append(')');
                break;
            case TupleShape tuple:
                text.Append("tuple(");
                foreach (var item in tuple.Items)
                {
                    Write(text, item.Type);
                    Reasons(text, item.NullableBecause);
                    text.Append(';');
                }

                text.Append(')');
                break;
            case RecordShape record:
                text.Append("record(");
                Write(text, record.Values);
                Reasons(text, record.ValuesNullableBecause);
                text.Append(')');
                break;
            case UnionShape union:
                text.Append("union(");
                foreach (var variant in union.Variants)
                {
                    Write(text, variant);
                    text.Append(';');
                }

                text.Append(')');
                break;
            case ObjectShape obj:
                text.Append("object(");
                foreach (var field in obj.Fields)
                {
                    text.Append(field.Name.Length).Append(':').Append(field.Name).Append(field.Presence);
                    Write(text, field.Type);
                    Reasons(text, field.NullableBecause);
                    text.Append(';');
                }

                text.Append(')');
                break;
            case JsonTextShape json:
                text.Append("text(");
                if (json.Content is { } content)
                    Write(text, content);
                text.Append(')');
                break;
            case RecursiveShape recursive:
                text.Append("node(").Append(recursive.Function).Append(')');
                break;
            case NeverShape never:
                text.Append("never(").Append(never.At).Append(')');
                break;
            case UnknownShape unknown:
                text.Append("unknown(").Append(unknown.Reason).Append(',').Append(unknown.At).Append(')');
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown shape.");
        }
    }

    private static void Reasons(StringBuilder text, IReadOnlyList<IrNullReason> reasons)
    {
        text.Append('[');
        foreach (var reason in reasons)
            text.Append(reason.Rule).Append('@').Append(reason.At).Append(',');
        text.Append(']');
    }
}
