namespace Haruspex.Ir;

public static partial class IrFit
{
    private sealed partial class ObjectWalk
    {
        /// <summary>
        /// A table-valued parameter, judged by the definitions the two documents carry (0.25): what the application sends, rows of
        /// its document's definition, must be what the procedure's takes. Each finding is refused, with the server's error.
        /// </summary>
        /// <remarks>
        /// Observed through the mssql driver (<c>ParameterObservationTests</c>, and <c>BaselineFitTests</c> for each change):
        /// another type, even of the same columns, fails the call (206), and one the database lacks fails it (2715); the columns
        /// are matched by place, so another number of them fails it (500) and a column renamed or moved takes another's value
        /// without an error; a column that became <c>NOT NULL</c> refuses NULL (515), a CHECK added a row that breaks it (547), and
        /// a key added a row that shares it (2627). A wider type, a column that became nullable, a CHECK or key removed, and a
        /// default added or removed (a row sent never takes one) each fit.
        /// </remarks>
        private void TableValued(IrPathSegment[] path, IrParameter known, IrParameter met)
        {
            var was = known.TableType is { } a ? Application.TableTypes.GetValueOrDefault(a) : null;
            var now = met.TableType is { } b ? Database.TableTypes.GetValueOrDefault(b) : null;
            if (was is null || now is null)
            {
                Add(findings, IrChangeKind.TableTypeChanged, path, IrFitVerdict.Refused, "the application sends a value of another kind than the procedure takes",
                    was is null ? null : IrClaimText.TableType(was), now is null ? null : IrClaimText.TableType(now));
                return;
            }

            if (!string.Equals(was.Bracketed, now.Bracketed, StringComparison.OrdinalIgnoreCase))
            {
                Add(findings, IrChangeKind.TableTypeChanged, path, IrFitVerdict.Refused,
                    "the application sends a table of the type it names, which the procedure no longer takes (error 206), even of the same columns", IrClaimText.TableType(was), IrClaimText.TableType(now));
                return;
            }

            // A type no row can be sent in is unknown in both, and fits only unchanged: the application can then only leave it out.
            if (known.Type is IrUnknownType || met.Type is IrUnknownType)
            {
                if (!(known.Type is IrUnknownType && met.Type is IrUnknownType && was.Defines(now)))
                    Add(findings, IrChangeKind.TableTypeChanged, path, IrFitVerdict.Refused, "the procedure's type of it can't be shown to take what the application may send", IrClaimText.TableType(was), IrClaimText.TableType(now));
                return;
            }

            if (Refusal(was, now) is { } why)
            {
                Add(findings, IrChangeKind.TableTypeChanged, path, IrFitVerdict.Refused, why, IrClaimText.TableType(was), IrClaimText.TableType(now));
                return;
            }

            // Each column's type and nullability: what the application sends must be what the procedure's column takes.
            for (var i = 0; i < was.Columns.Count; i++)
            {
                var (sent, taken) = (was.Columns[i], now.Columns[i]);
                IrPathSegment[] at = [.. path, new IrPathItems(), new IrPathKey(sent.Name)];
                if (sent.Nullable && !taken.Nullable)
                    Add(findings, IrChangeKind.BecameNonNull, at, IrFitVerdict.Refused, "the application may send NULL, which the column no longer takes (error 515)");
                if (sent.Type is { } given && taken.Type is { } accepting)
                    new Values(this, Flow.Parameters, findings).Type(at, accepting, given);
                else if (sent.Sql != taken.Sql)
                    Add(findings, IrChangeKind.SqlTypeChanged, at, IrFitVerdict.Refused, "the column's type isn't modelled, so it can't be shown to take what the application may send", sent.Sql, taken.Sql);
            }
        }

        /// <summary>
        /// Why the rows the application sends, of the definition <paramref name="was"/>, may be refused by <paramref name="now"/>
        /// as a whole, whatever their columns' values; null when nothing but the columns' types is left to judge.
        /// </summary>
        private static string? Refusal(IrTableType was, IrTableType now)
        {
            if (was.Columns.Count != now.Columns.Count)
                return "the application sends a table of as many columns as it knew, which the procedure's type no longer has (error 500)";
            for (var i = 0; i < was.Columns.Count; i++)
            {
                var (sent, taken) = (was.Columns[i], now.Columns[i]);
                if (!string.Equals(sent.Name, taken.Name, StringComparison.OrdinalIgnoreCase))
                    return $"the server matches columns by place, so the application's {sent.Name} would be taken as {taken.Name} without an error";
                if (taken.Generated is not null && sent.Generated is null)
                    return $"its column {taken.Name} is now one the server fills, so no row can be sent";
                if (sent.Collation != taken.Collation)
                    return $"its column {taken.Name} compares strings under another collation, which may change what a key refuses or what a character becomes";
            }

            if (now.Checks.FirstOrDefault(c => !was.Checks.Contains(c, StringComparer.Ordinal)) is { } check)
                return $"a row the application may send may break its condition {check} (error 547)";
            if (now.Keys.FirstOrDefault(k => !was.Implies(k)) is { } key)
            {
                return key.IgnoresDuplicates
                    ? $"a row the application may send may share its key ({string.Join(", ", key.Columns)}) with another, and be dropped without an error"
                    : $"a row the application may send may share its key ({string.Join(", ", key.Columns)}) with another (error 2627)";
            }

            return null;
        }
    }
}
