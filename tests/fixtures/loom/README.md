# Fixture: loom

An invented weaving workshop's patterns, for the `xml` type: each pattern's draft is XML, and its notes are XML that may be
missing or an empty instance, which is not NULL. A checked draft and a card are typed by XML schema collections. Read in a rowset and through `FOR JSON`, sent as parameters, converted both
ways, and read by its methods
([decision 0133](../../../docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md)). No part of it comes from
a real system. The seed reaches a draft with every attribute the procedures read, one with none of them, drafts longer and shorter than
20 characters, a name that isn't XML, and notes that are present, empty and NULL.

| Procedure | Covers |
|---|---|
| `loom.get_pattern` | An `xml` column never NULL and one that may be, in a rowset: text, `""` for an empty instance |
| `loom.get_pattern_json` | The same columns through `FOR JSON`: a JSON string of the same text |
| `loom.pattern_facts` | `value()` of several type arguments (a length, digits, none, bracketed, in capitals), always nullable; `exist()`, NULL only where the XML is; `query()`, unknown; `CAST`, `CONVERT` with and without a style and `TRY_CONVERT` to `xml`, and `CAST` and `TRY_CAST` from it to text; `COALESCE`, `ISNULL` and `CASE` of `xml` and text, which are `xml` |
| `loom.save_draft` | `xml` parameters, one with a default, written to the table (the draft only when given); `value()` and `exist()` of a parameter |
| `loom.check_drafts` | Columns, a variable and a parameter of `xml` typed by an XML schema collection, each named with its schema, without it in a table (found in `dbo`) and without it in a module (found in the module's schema, then `dbo`), one `DOCUMENT`: each `xml`, its type spelt with the collection it is bound to |
| `loom.count_threads` | `nodes()` of a column, unknown |
| `loom.list_ends` | `nodes()` of a parameter, unknown |
