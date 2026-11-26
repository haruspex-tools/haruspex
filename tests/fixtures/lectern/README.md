# Fixture: lectern

An invented reading room for descriptions held in extended properties: which result fields take the description of a column,
a procedure's and its parameter's own, and what each output makes of a description's text. A field that is a column,
unchanged, by name, through `*`, a view or a derived table, takes the column's description, and a view's own description of
its column replaces the table's; a field computed from a column, or read from two columns by two paths, takes none. The
descriptions hold `*/`, quotation marks, a backslash, a line break and a leading `@`, which must not break a comment or a
string, nor become a tag. The scripts set them with named, positional and identifier arguments, and set two more on what no
output has (the schema and a constraint), which are passed over. No part of it comes from a real system
([decision 0128](../../../docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md)).

| Procedure | Covers |
|---|---|
| `library.find_book` | a procedure's and its parameter's description; a column by name takes its own, `UPPER(title)` and `pages + 0` none, a description over two lines |
| `library.all_books` | every column through `*`, each with its description or none |
| `library.on_shelf` | a view's own description of its column, which replaces the table's, kept through a derived table |
| `library.books_json` | the keys of `FOR JSON PATH`: one that is a column takes its description, `LEN(title)` none |
| `library.title_or_name` | two paths that read two columns into one field, `reader.name` and `book.title`: neither description |
