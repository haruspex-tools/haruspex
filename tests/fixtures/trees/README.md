# Fixture: trees

Scalar functions that return JSON, and functions that call themselves to build trees of any depth: the way T-SQL
builds a nested document whose depth is in the data. Needs SQL Server 2022 or later (`json_object`).

Two invented domains, with the seed in `tests/fixtures/trees/seed.sql`:

- **A bill of materials** (`bom`): parts and assemblies, an assembly's components each with their own components.
  The seed goes four levels deep, uses one part in three assemblies, and has an assembly with no components yet.
- **A catalogue** (`cat`): categories whose contents hold their items and subcategories, through two functions that
  call each other.

| Object | Covers |
|---|---|
| `bom.components` | a function that calls itself through `json_query`: a recursive named type (`ComponentsNode`), whose `components` key is left out at a leaf (FOR JSON over no rows is NULL) |
| `cat.category_tree`, `cat.category_contents` | mutual recursion: two recursive named types, one an array of objects (FOR JSON), one an object (`json_object … ABSENT ON NULL`, never NULL) |
| `cat.item_card` | not recursive: FOR JSON `WITHOUT_ARRAY_WRAPPER` through a variable, NULL for no item |
| `cat.item_label` | a constructor with `RETURNS NULL ON NULL INPUT`: non-null parameters inside, NULL for a null argument |
| `cat.item_tag` | a constructor without it: a parameter inside is null when the argument is, and its key is kept as `null` |
| `bom.get_assembly` | one part and its tree; absent when there is no such part |
| `bom.list_parts` | every part's tree, live through `json_query` and as escaped text from a plain call |
| `cat.get_category` | the two mutually recursive functions from a procedure |
| `cat.list_items` | the non-recursive functions with and without `json_query`; null-input; a null parameter inside a body |
| `cat.item_rows` | a JSON function as a rowset column: text whose content the IR states |

The types do not say how deep a tree goes: SQL Server stops at 32 nested calls with error 217.
