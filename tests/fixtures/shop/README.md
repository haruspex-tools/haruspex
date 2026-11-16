# Fixture: shop

An invented shop with a config file (`haruspex.json`) that uses every part of it: selection per schema with
`include` and `exclude`, a schema `prefix` and a schema's own naming rule, a procedure named by override, a nested type
named by a path override, `nullable: false` with its reason, `values: "exact"`, a JSON column typed by an imported
schema, strict objects, every emitter (`zod`, `json-schema` and `mssql`, the functions, with `serverOnly`), and the top-level
naming rule `#` → `Row`. `ConfigCliTests` runs it end to end through the CLI, `FixtureSnapshotTests` holds that output to `expected/`, and `EmitCheckTests` compiles
the result with `tsc`, with `@shop/types` mapped to `tests/emit-check/src/shop-types.ts`.

Its `queries` folder holds two query files of the application, which the config file names (`"queries": "queries"`): each is
typed with the procedures, named from its file by the same rules, and given a function beside theirs.

| Query file | Name | Parameters |
|---|---|---|
| `queries/products_under.sql` | `ProductsUnder` | `@max_price decimal(38,19)` (compared with `<=`), `@status varchar(8)`, `@skip` and `@take` `bigint` (`OFFSET … FETCH`) |
| `queries/basket_card.sql` | `BasketCard` | `@label nvarchar(40)` (declared, since nothing decides it), `@basket_id int` |

It is not deployed by the differential tests (its `json` column needs nothing they don't already cover); the overrides
are proven runtime-checked there on the `checks` fixture instead (`OverrideAcceptanceTests`).

| Procedure | Selected | Name |
|---|---|---|
| `shop.get_basket` | yes | `Basket` (override), its lines `BasketItem` (path override) |
| `shop.list_products` | yes | `ListProducts` |
| `shop.[basket_line#]` | yes | `BasketLineRow` (`#$` → `Row`) |
| `shop.recalculate_internal` | excluded | |
| `shop_admin.report_sales` | yes | `AdminSales` (prefix `Admin`, the schema's rule `^report_` → nothing) |
| `shop_admin.purge_baskets` | no | |
