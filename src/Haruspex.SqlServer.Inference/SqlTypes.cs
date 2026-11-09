using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>Maps SQL Server types to the IR's logical types, keeping the SQL type as written.</summary>
/// <remarks>
/// The model says what a type is (<see cref="SqlType"/>); what it becomes in the IR is inference's choice, so the
/// mapping lives here (slice 2b split it from the model's type).
/// </remarks>
internal static class SqlTypes
{
    public static Shape ToShape(SqlType? type, SourceLocation at)
    {
        if (type is not { Kind: SqlTypeKind.BuiltIn })
        {
            // Alias types (including sysname), XML and CLR types are not modelled yet.
            var name = type?.Name ?? "(none)";
            return new UnknownShape(IrUnknownReasons.UnsupportedSqlType, $"The type {name} is not modelled yet.", at);
        }

        var text = Text(type);
        return type.Name switch
        {
            "tinyint" => Scalar(IrScalarKind.Number, IrFormats.Uint8, text),
            "smallint" => Scalar(IrScalarKind.Number, IrFormats.Int16, text),
            "int" => Scalar(IrScalarKind.Number, IrFormats.Int32, text),
            "bigint" => Scalar(IrScalarKind.Number, IrFormats.Int64, text),
            "decimal" or "numeric" or "money" or "smallmoney" => Scalar(IrScalarKind.Number, IrFormats.Decimal, text),
            "float" or "real" => Scalar(IrScalarKind.Number, IrFormats.Float, text),
            "bit" => Scalar(IrScalarKind.Boolean, null, text),
            "char" or "varchar" or "nchar" or "nvarchar" => Scalar(IrScalarKind.String, null, text),
            "date" => Scalar(IrScalarKind.Date, null, text),
            "time" => Scalar(IrScalarKind.Time, null, text),
            "datetime" or "datetime2" or "smalldatetime" => Scalar(IrScalarKind.Datetime, IrFormats.Local, text),
            "datetimeoffset" => Scalar(IrScalarKind.Datetime, IrFormats.Offset, text),
            "uniqueidentifier" => Scalar(IrScalarKind.Uuid, null, text),
            _ => new UnknownShape(IrUnknownReasons.UnsupportedSqlType, $"The type {text} is not modelled yet.", at),
        };
    }

    private static ScalarShape Scalar(IrScalarKind kind, string? format, string sql) => new(new IrScalar(kind, format, sql));

    /// <summary>The type as written, normalised: <c>decimal(19,2)</c>, <c>nvarchar(max)</c>.</summary>
    private static string Text(SqlType type)
    {
#pragma warning disable CA1308 // The IR writes SQL type names in lower case, as T-SQL is usually written; this is not a comparison.
        return type.Arguments.Count == 0
            ? type.Name
            : type.Name + "(" + string.Join(",", type.Arguments.Select(a => a.ToLowerInvariant())) + ")";
#pragma warning restore CA1308
    }
}
