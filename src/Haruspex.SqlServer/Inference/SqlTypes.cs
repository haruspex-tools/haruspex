using Haruspex.Ir;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>Maps SQL Server types to the IR's logical types, keeping the SQL type as written.</summary>
internal static class SqlTypes
{
    public static Shape ToShape(DataTypeReference? type, SourceLocation at)
    {
        if (type is not SqlDataTypeReference sql)
        {
            // Alias types (including sysname), XML and CLR types are not modelled yet.
            var name = type?.Name?.BaseIdentifier?.Value ?? "(none)";
            return new UnknownShape(IrUnknownReasons.UnsupportedSqlType, $"The type {name} is not modelled yet.", at);
        }

        var text = Text(sql);
        return sql.SqlDataTypeOption switch
        {
            SqlDataTypeOption.TinyInt => Scalar(IrScalarKind.Number, IrFormats.Uint8, text),
            SqlDataTypeOption.SmallInt => Scalar(IrScalarKind.Number, IrFormats.Int16, text),
            SqlDataTypeOption.Int => Scalar(IrScalarKind.Number, IrFormats.Int32, text),
            SqlDataTypeOption.BigInt => Scalar(IrScalarKind.Number, IrFormats.Int64, text),
            SqlDataTypeOption.Decimal or SqlDataTypeOption.Numeric or SqlDataTypeOption.Money or SqlDataTypeOption.SmallMoney
                => Scalar(IrScalarKind.Number, IrFormats.Decimal, text),
            SqlDataTypeOption.Float or SqlDataTypeOption.Real => Scalar(IrScalarKind.Number, IrFormats.Float, text),
            SqlDataTypeOption.Bit => Scalar(IrScalarKind.Boolean, null, text),
            SqlDataTypeOption.Char or SqlDataTypeOption.VarChar or SqlDataTypeOption.NChar or SqlDataTypeOption.NVarChar
                => Scalar(IrScalarKind.String, null, text),
            SqlDataTypeOption.Date => Scalar(IrScalarKind.Date, null, text),
            SqlDataTypeOption.Time => Scalar(IrScalarKind.Time, null, text),
            SqlDataTypeOption.DateTime or SqlDataTypeOption.DateTime2 or SqlDataTypeOption.SmallDateTime
                => Scalar(IrScalarKind.Datetime, IrFormats.Local, text),
            SqlDataTypeOption.DateTimeOffset => Scalar(IrScalarKind.Datetime, IrFormats.Offset, text),
            SqlDataTypeOption.UniqueIdentifier => Scalar(IrScalarKind.Uuid, null, text),
            _ => new UnknownShape(IrUnknownReasons.UnsupportedSqlType, $"The type {text} is not modelled yet.", at),
        };
    }

    private static ScalarShape Scalar(IrScalarKind kind, string? format, string sql) => new(new IrScalar(kind, format, sql));

    /// <summary>The type as written, normalised: <c>decimal(19,2)</c>, <c>nvarchar(max)</c>.</summary>
    private static string Text(SqlDataTypeReference type)
    {
#pragma warning disable CA1308 // The IR writes SQL type names in lower case, as T-SQL is usually written; this is not a comparison.
        var name = type.SqlDataTypeOption.ToString().ToLowerInvariant();
        return type.Parameters.Count == 0
            ? name
            : name + "(" + string.Join(",", type.Parameters.Select(p => p.Value.ToLowerInvariant())) + ")";
#pragma warning restore CA1308
    }
}
