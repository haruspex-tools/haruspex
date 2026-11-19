# Fixture: gallery

An invented shop's product images, for binary values: `binary`, `varbinary` (of a length and of `max`), `rowversion` and the
deprecated `image`, read in a rowset and through `FOR JSON`, sent as parameters (one `OUTPUT`), and made by the expressions that
make them ([decision 0081](../../../docs/decisions/0081-binary-values-are-a-kind-of-their-own-counted-in-bytes.md)). No part of it
comes from a real system. The seed reaches a thumbnail as long as its column allows, an original past 8,000 bytes, which `FOR JSON`
writes as base64 of more than 8,000 characters, a thumbnail of no bytes, and NULL.

| Procedure | Covers |
|---|---|
| `gallery.get_image` | Each binary type in a rowset: a `Buffer` of its length |
| `gallery.get_image_json` | The same columns through `FOR JSON`: base64 text, padded, `""` for no bytes, a NULL left out |
| `gallery.add_image` | Binary parameters, one of `max` with a default; `HASHBYTES` writing a `binary(32)` checksum; `DATALENGTH` |
| `gallery.sign_image` | A `rowversion` parameter and a `binary(32)` `OUTPUT` one |
| `gallery.image_facts` | The expressions that make binary values: conversions with and without a style, `COALESCE`, `ISNULL`, `CASE`, `+`, `CONCAT`, `SUBSTRING`, `LEFT`, `HASHBYTES` (of a literal algorithm, a digest of its length; of one chosen by row, which may be NULL), `COMPRESS` and `DECOMPRESS` |
| `gallery.list_versions` | A `UNION ALL` of a `binary(32)` and a `varbinary(8000)`: `varbinary(8000)` |
| `gallery.list_scans` | An `image` column in a rowset, and its `DATALENGTH` |
| `gallery.list_scans_json` | An `image` column through `FOR JSON`: base64 text, as `varbinary(max)`'s; absent over no scans |
