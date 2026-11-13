# Fixture: plots

An invented allotment whose point is where things are in a script, not what its procedures return. No part of it comes
from a real system. Every other fixture keeps one object to a file, saved as UTF-8 without a byte order mark and with LF
line endings; this one lays its scripts out as database projects and scripts saved by hand often are, so that every
position haruspex reports from them (each `at` in the IR, and each location the model records) is checked against a
layout that could move it:

| Script | Layout |
|---|---|
| `database/plots.sql` | UTF-8 with a byte order mark and CRLF line endings; the schema, both tables, a view, an inline function and two procedures, each in its own batch after `GO`, some after comments and blank lines |
| `database/later.sql` | UTF-8 with a byte order mark and CRLF line endings; two procedures after blank lines, one indented with spaces |
| `database/harvest.sql` | UTF-16 (little-endian) with a byte order mark, as SQL Server Management Studio saves a script; CRLF line endings, then LF, and one lone CR inside a procedure; objects after a comment block, after tabs, after `go` in lower case and after `GO` followed by a comment |

Git keeps these scripts byte for byte (`.gitattributes`), and so do editors (`.editorconfig`).

The seed (`tests/fixtures/plots/seed.sql`) gives a bed with two crops sown, one harvested and one not; a bed with none (the
outer join's unmatched row) and no area; a bed with one crop, harvested; and a bed with no area and one crop, harvested.

| Procedure | Covers |
|---|---|
| `plot.list_beds` | a rowset with a correlated `COUNT(*)` |
| `plot.bed_with_crops` | a `LEFT OUTER JOIN`, whose unmatched row makes the sowing's columns nullable |
| `plot.crops_of` | a view defined in another script |
| `plot.bed_json` | `FOR JSON` over an inline function defined in another script |
| `plot.harvest_dates` | a select list split by a lone CR, then a `LEFT OUTER JOIN` |
| `plot.unharvested` | a procedure indented with tabs, after `go` in lower case, with a `LEFT OUTER JOIN` after a tab |
| `plot.harvested_area` | `SUM` grouped, after `GO` with a comment |
