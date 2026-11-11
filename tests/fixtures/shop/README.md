# Fixture: shop

An invented shop with a config file (`haruspex.json`) that uses every part of it: selection per schema with
`include` and `exclude`, a schema `prefix` and a schema's own naming rule, a procedure named by override, a nested type
named by a path override, `nullable: false` with its reason, `values: "exact"`, a JSON column typed by an imported
schema, strict objects, and the top-level naming rule `#` → `Row`. `ConfigCliTests` runs it end to end through the CLI, `FixtureSnapshotTests` holds that output to `expected/`, and `EmitCheckTests` compiles
the result with `tsc`, with `@shop/types` mapped to `tests/emit-check/src/shop-types.ts`.

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
