# Fixture: market

An invented market whose config file (`haruspex.json`) splits the output into a module per schema (`"split": "schema"`),
with every emitter. `FixtureSnapshotTests` runs it through the CLI and holds every file it writes to `expected/`, and
`EmitCheckTests` compiles and lints each module and the shared file, alone and together.

| Procedure or query file | Module | What it shows |
|---|---|---|
| `sales.get_order` | `sales` | its rows named `OrderTotal` by a path override |
| `sales.list_orders` | `sales` | rows of `OrderTotal`'s shape, in the same module |
| `shop.get_product` | `shop` | named sets; its `orders` rows are `OrderTotal`'s shape, so `OrderTotal` is in the shared file |
| `admin.list_users`, `admin.list_retired_users` | `admin` | rows of one shape with no declared name |
| `shop.list_staff` | `shop` | rows of admin's shape, so the shape is in the shared file as an internal `shape…` constant, which both modules import |
| `queries/top_products.sql` | `shop` | a query file put in a module by `-- @module shop` |
| `queries/order_count.sql` | `queries` | a query file that names no module, in the default one |

It is not deployed by the differential tests: what it shows is where each definition is written, which needs no server.
`MssqlFunctionTests` calls the functions of a split module on the server instead.
