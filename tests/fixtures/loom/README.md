# Fixture: loom

An invented weaving workshop's patterns, for the `xml` type: each pattern's draft is XML, and its notes are XML that may be
missing or an empty instance, which is not NULL. A checked draft and a card are typed by XML schema collections. Read in a rowset and through `FOR JSON`, sent as parameters, converted both
ways, and read by its methods, by `nodes()`, `FOR XML` and `OPENXML`
([decision 0133](../../../docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md)). No part of it comes from
a real system. The seed reaches a draft with every attribute the procedures read, one with none of them, drafts longer and shorter than
20 characters, a name that isn't XML, and notes that are present, empty and NULL. A pattern with no threading has no ends, so `nodes()` and the `FOR XML` subqueries over them give no rows, and one has an end with no colour.

| Procedure | Covers |
|---|---|
| `loom.get_pattern` | An `xml` column never NULL and one that may be, in a rowset: text, `""` for an empty instance |
| `loom.get_pattern_json` | The same columns through `FOR JSON`: a JSON string of the same text |
| `loom.pattern_facts` | `value()` of several type arguments (a length, digits, none, bracketed, in capitals), always nullable; `exist()`, NULL only where the XML is; `query()`, unknown; `CAST`, `CONVERT` with and without a style and `TRY_CONVERT` to `xml`, and `CAST` and `TRY_CAST` from it to text; `COALESCE`, `ISNULL` and `CASE` of `xml` and text, which are `xml` |
| `loom.save_draft` | `xml` parameters, one with a default, written to the table (the draft only when given); `value()` and `exist()` of a parameter |
| `loom.check_drafts` | Columns, a variable and a parameter of `xml` typed by an XML schema collection, each named with its schema, without it in a table (found in `dbo`) and without it in a module (found in the module's schema, then `dbo`), one `DOCUMENT`: each `xml`, its type spelt with the collection it is bound to |
| `loom.count_threads` | `nodes()` of a column: `value()`, always nullable, and `exist()` and `query()` of its node, never NULL; a pattern with no ends gives no rows |
| `loom.list_ends` | `nodes()` of a parameter, NULL included, which gives no rows |
| `loom.list_threading` | `OUTER APPLY` of `nodes()`, and of `nodes()` of its node: NULL where the path finds nothing ([decision 0137](../../../docs/decisions/0137-xml-s-nodes-query-for-xml-and-openxml-are-typed-as-the-server-gives-them.md)) |
| `loom.thread_colours` | `FOR XML` subqueries: `PATH` with `TYPE`, `xml`; `PATH`, `RAW` with `ROOT` and `AUTO` with `ELEMENTS` without it, `nvarchar(max)`; the `STUFF` idiom; NULL over no rows, and never NULL over one, as `COUNT(*)`'s |
| `loom.colour_summary` | A CTE and a derived table whose query is `FOR XML`: exactly one row, its column NULL when the query returns no rows |
| `loom.read_card` | `OPENXML … WITH` between `sp_xml_preparedocument` and `sp_xml_removedocument`: each column its declared type, a string cut short and a `char` padded, a decimal rounded, NULL where its path finds nothing |
