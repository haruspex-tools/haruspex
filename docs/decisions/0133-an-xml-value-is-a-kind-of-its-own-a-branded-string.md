# An `xml` value is a kind of its own, a branded string

**Date:** 26 Nov 2026

**Decision.** The IR has a scalar kind for XML text, `xml`, which SQL Server's `xml` is, typed by a schema collection or not.
It has no format and no facets: an `xml` value has no length a type declares (it holds up to 2 GB), no digits, bounds or value
set, and a reader refuses an `xml` value that says any of them. Its `sql` is the type, spelt with the schema collection
it is bound to (`xml`, `xml(shop.spec_schema)`, `xml(DOCUMENT shop.spec_schema)`). A document that holds an
`xml` value anywhere is IR 0.34, and one that holds none is labelled as before, by the rest of what it says (decision 0072's
labelling by content, as [decision 0081](0081-binary-values-are-a-kind-of-their-own-counted-in-bytes.md) did for `bytes`); the
reader refuses an `xml` value before 0.34, and a document labelled 0.34 that holds none.

Every target writes an `xml` value as the text the server serialises it to, which is what arrives in every encoding
(observed): the mssql driver hands over a rowset's `xml` column as a string, typed by a collection or not, `""` for an empty
instance and `null` for NULL, and `FOR JSON` and the JSON constructors write it as a JSON string of the same text. In Zod a
result's `xml` value is a string branded Xml, `z.string().brand<"Xml">()`: lossless, needing no parser, and not interchangeable
with plain text (the maintainer, 21 Nov 2026). The brand is written where each value is, not declared once in a module, so that
no procedure or type the module declares can take its name, and every module's brand is the same TypeScript type, since a brand
is a property of the type by its name. A parameter is sent as text with the driver's `sql.Xml()` type and checked only as a
string, since the server refuses text that isn't well formed (error 9400, observed) and haruspex adds no parser to check it
first; a value a result handed over, being a string, is accepted as it is. In JSON Schema it is `"type": "string"` with `"contentMediaType": "application/xml"`, an
annotation that validates nothing more.

*What is typed in SQL.*

- A column, variable or parameter of type `xml`, as any column, variable or parameter is, its nullability included.
- `CAST(… AS xml)`, `CONVERT(xml, …)` with or without a style, and `CAST` to a typed `xml`: NULL only when the operand is
  (observed). `TRY_CAST` and `TRY_CONVERT` to `xml` are NULL for text that isn't well formed too, so always nullable
  (observed). A conversion from `xml` to text or binary keeps the target's type and the operand's null rule; to text too short
  for the instance it fails (observed), so the length the target declares holds.
- `CASE`, `IIF`, `COALESCE`, `ISNULL` and `UNION ALL` of `xml` with text give `xml`, by type precedence (observed), the text
  converted.
- `x.value('path', 'type')`: the type its literal second argument names, whose facets hold as a conversion's do (a string is
  cut to the type's length, silently, as its type counts it: `nvarchar(3)` to 3 UTF-16 code units, `varchar(3)` to 3 bytes, which
  in a UTF-8 collation may hold fewer characters: observed), and always nullable, with a new null rule, `xml-value`: NULL where the path finds no
  node or the instance is NULL (observed). A type argument the server takes but turns into another type (`json` and `vector(n)`
  are `varchar(max)`: observed), or one not built in, leaves it unknown; the server refuses `xml`, `text`, `ntext`, `image`,
  `sql_variant` and the CLR types (observed). Nullable unless proven: no proof is modelled yet.
- `x.exist('path')`: `bit`, NULL exactly when the instance is NULL (observed: a NULL `sql:variable` or `sql:column` in the path,
  or an empty instance, gives 0), so never null over an instance that never is.
- `x.query(…)` and `x.nodes(…)` stay unknown with a reason: what they return is XML that the path and, later, the collection
  shape. `OPENXML` and `WITH XMLNAMESPACES` stay unknown as before.

*XML schema collections.* `CREATE XML SCHEMA COLLECTION` and `ALTER XML SCHEMA COLLECTION … ADD` are read from scripts, and
`SqlXmlSchemaCollection` elements from a package, into the model, each with the text of its schemas, and each typed `xml`
column, variable and parameter is bound to its collection by the rules that find an alias type (observed: in a module, the
module's schema then `dbo`; elsewhere, the default schema, not the table's). Its `sql` is spelt with the collection it is bound
to (`xml(DOCUMENT dbo.card_schema)`), as an alias type's column is spelt with its base type, so one database read from its
scripts or from its package gives one output. Nothing is typed from a collection yet; that is the follow-up, which needs the
bound collections. A typed column whose collection the source doesn't define is still `xml`, spelt as written. A fit refuses
a typed `xml` parameter, since a collection that narrows an element fails the old call under the same name (6926), and takes
an untyped one, which accepts any well-formed XML.

*`FOR XML`.* A result set a procedure or a query file returns as `FOR XML` stays unknown, and a note, HAR2026, says where each
is: the driver joins its rows into one string and never parses it (observed), so the application receives XML it must parse
itself, and `FOR JSON` builds the same data, which haruspex types. A `FOR XML` subquery builds a value, not a result set, so it
has no note.

*An `xml` `OUTPUT` parameter.* The mssql driver, as of 12.7.14, can't receive one: it declares the parameter as `nvarchar`, so
bound as `sql.Xml()` or as `sql.NVarChar(sql.MAX)` the call fails with error 257 ("Implicit conversion from data type xml to
nvarchar is not allowed", observed), typed by a collection or not. The IR still types the parameter, since the claim is the
server's; the mssql module leaves out that procedure's function, with a warning (HAR5206), and writes the rest.

**Context.** Until now `xml` was not modelled: every `xml` column, parameter, conversion and XML method was an unknown
(`unsupported-sql-type` or `unsupported-expression`), most of them in one corpus of [the harvest](../corpus-harvest.md). An application reading an
`xml` column had nothing checked of it, though the server guarantees it is well-formed XML text.

**Alternatives.**
- *A string with a format, `xml`.* A string's facets (a length in UTF-16 code units, a value set under a collation) would be
  wrong for it, as they were for bytes, and an emitter that reads a string's length would have to know to ignore it. A kind of
  its own keeps the rule that a kind says how a value arrives.
- *A plain string.* Lossless, but it throws away what the server guarantees, and lets an application pass any text where XML
  is meant.
- *An object mapping or a DOM.* No mapping is natural (attributes or elements, one or many, mixed content, order, namespaces),
  and a DOM needs a parser in Node, which haruspex would have to choose; a DOM is left to a setting a user opts into, later.
- *Type `.query()` as `xml`.* Sound, but the maintainer chose to leave it for the typing from collections, where its result
  can be said more exactly.
- *Label every document 0.34.* Every committed IR and every module's header would change in a run that meets no `xml` value.

**Reason.** A kind of its own lets each emitter write the value exactly as it arrives, keeps a string's rules away from it, and
labelling by content leaves every document without one as it was.
