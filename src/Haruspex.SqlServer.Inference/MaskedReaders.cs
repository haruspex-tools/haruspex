using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Which functions may read a masked column, directly or through the views, functions and synonyms they name: a call of a scalar
/// one returns its mask to a caller without <c>UNMASK</c>, whatever its body does with the value (observed:
/// <c>A_scalar_function_that_reads_a_masked_column_returns_its_mask</c>).
/// </summary>
/// <remarks>
/// <para>
/// It is an over-approximation, by name: a body may read a masked column when it names, anywhere and in any part of a name, a
/// table with a masked column (of this database or another the source holds) or a view, function or synonym that may read one,
/// found until nothing more is added. Binding each body to say exactly which columns it reads would be more precise, but a name
/// that matches when the object is another only masks a result that wasn't, which claims less. A body the source doesn't hold (an
/// encrypted function's) may read any table, so it may read a masked one when the model has any. A CLR function reads through a
/// connection of its own, and returns a value its code computes, which is never masked as a whole.
/// </para>
/// <para>
/// A view and an inline function need none of this: their columns are read through their query, whose values carry their masks
/// (<see cref="Value.MaskedBy"/>). Only a scalar function's call, whose body is not read as an expression, is masked by this.
/// </para>
/// </remarks>
/// <param name="model">The model.</param>
internal sealed class MaskedReaders(DatabaseModel model)
{
    private readonly Lazy<Dictionary<ObjectName, string>> readers = new(() => Find(model));

    /// <summary>A masked column <paramref name="function"/> may read, as <c>schema.table.column</c>; null when it reads none.</summary>
    public string? Of(Function function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return readers.Value.GetValueOrDefault(function.Name);
    }

    private static Dictionary<ObjectName, string> Find(DatabaseModel model)
    {
        // The masked column each name may stand for: a table's own, by its name alone, as a body may write it.
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in model.Tables.Concat(model.Databases.Values.SelectMany(d => d.Tables)))
        {
            if (table.Columns.FirstOrDefault(c => c.IsMasked) is { } column)
                named.TryAdd(table.Name.Name, $"{table.Name}.{column.Name}");
        }

        var readers = new Dictionary<ObjectName, string>();
        if (named.Count == 0)
            return readers;
        var anyMask = named.Values.First();

        var bodies = model.Functions.Select(f => (Module)f).Concat(model.Views)
            .Select(m => (Module: m, Names: m is Function { } f && IsEncrypted(f) ? null : NamesIn(m)))
            .ToList();
        var synonyms = model.Synonyms.Select(s => (s.Name, Target: s.For.Name)).ToList();

        bool changed;
        do
        {
            changed = false;
            foreach (var (module, names) in bodies.Where(b => !readers.ContainsKey(b.Module.Name)))
            {
                var by = names is null ? anyMask : names.Select(n => named.GetValueOrDefault(n)).FirstOrDefault(c => c is not null);
                if (by is null)
                    continue;
                readers[module.Name] = by;
                named.TryAdd(module.Name.Name, by);
                changed = true;
            }

            foreach (var (name, target) in synonyms.Where(s => !named.ContainsKey(s.Name.Name)))
            {
                if (named.GetValueOrDefault(target) is { } by)
                {
                    named[name.Name] = by;
                    changed = true;
                }
            }
        }
        while (changed);

        return readers;
    }

    private static bool IsEncrypted(Function function) => function switch
    {
        ScalarFunction { Body: EncryptedBody } or TableFunction { Body: EncryptedBody } => true,
        _ => false,
    };

    /// <summary>Every identifier in a module's definition: the names it may read; none for a CLR body or a view without its query.</summary>
    private static HashSet<string> NamesIn(Module module)
    {
        TSqlFragment? definition = module switch
        {
            ScalarFunction { Body: SqlBody body } => body.Statements,
            TableFunction { Body: SqlBody body } => body.Statements,
            InlineFunction inline => inline.Query,
            View view => view.Query,
            _ => null,
        };
        var identifiers = new Identifiers();
        definition?.Accept(identifiers);
        return identifiers.Found;
    }

    /// <summary>Collects the value of every identifier a walk of a tree meets.</summary>
    private sealed class Identifiers : TSqlFragmentVisitor
    {
        public HashSet<string> Found { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(Identifier node) => Found.Add(node.Value);
    }
}
