# Fixture: wide-world-importers

`FOR JSON PATH` over real tables: one order as an object, with its salesperson nested from dotted aliases and its lines as an
array from a correlated subquery. It is the first example in [the project's README](../../../README.md#examples-on-microsofts-sample-databases), and
holds that example to a real server, with the README's query file over the same tables.

Unlike the other fixtures, its tables are not invented. They are four of the tables of Microsoft's WideWorldImporters sample,
vendored in [`tests/corpus`](../../corpus/README.md) under the MIT licence, whose notice is in `license.txt` here. Each table's
script says where it comes from and what was left out to deploy it alone: defaults drawn from sequences, foreign keys to tables
not here, indexes and descriptions. None of those changes what a query returns, and a test holds that: the README's example is
inferred over the whole sample, and this fixture's snapshot must match it (`ShowcaseTests`).

The procedure, `Website.GetOrder`, the query file, `sql/getOrderLines.sql`, and the seed are the project's own; the rows are
invented.

| Object | Covers |
|---|---|
| `Website.GetOrder` | one order or none (`WITHOUT_ARRAY_WRAPPER` over a primary key); a nested object from a join; lines as an array, absent for an order with none; nullable columns left out of the JSON |
| `sql/getOrderLines.sql` | a query file: its parameter's type inferred from the column it is compared with; a nullable column as `null` in a row; an order with no lines. Its runs in `runs.json` call it through its generated function (`GeneratedFunctionTests`) |

`Sales` is also the `orders` fixture's schema, and both deploy into one database, so `schemas.sql` creates it only when it
isn't there.
