# Fixture: adventure-works

A scalar function that calls itself to build a tree with `FOR JSON PATH`, over real tables: a product's bill of materials, to
any depth. It is the second example in [the project's README](../../../README.md#two-examples-on-microsofts-samples), and holds that example to
a real server.

Unlike the other fixtures, its tables are not invented. They are two of the tables of Microsoft's AdventureWorks sample,
vendored in [`tests/corpus`](../../corpus/README.md) under the MIT licence, whose notice is in `license.txt` here, with the two
alias types they use. Each table's script says where it comes from and what was left out to deploy it alone: foreign keys to
tables not here, other indexes and descriptions. None of those changes what a query returns, and a test holds that: the README's
example is inferred over the whole sample, and this fixture's snapshot must match it (`ShowcaseTests`).

The function, the procedure and the seed are the project's own; the rows are invented. The seed builds a bicycle three levels
deep, with a component used in two assemblies, a component whose use has ended, and a product with no components.

| Object | Covers |
|---|---|
| `Production.GetComponents` | a function that calls itself through `JSON_QUERY`: a recursive named type, whose `components` key is left out at a leaf; a `CHECK` constraint's bound (`PerAssemblyQty >= 1.00`) as a minimum |
| `Production.GetProductTree` | one product and its tree, or none |

The types don't say how deep a tree goes: SQL Server stops at 32 nested calls with error 217.
