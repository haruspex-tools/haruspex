# Fixture: sets

An invented small ice rink for set operations, `CROSS JOIN`, `FULL OUTER JOIN` and `OPENJSON` (slice 6h). No part of it comes
from a real system. The seed (`Seeds/sets.sql`) makes every query of every set operation give a NULL somewhere (members
without an email or a level, a guest without a phone, a guest's lesson booking without a member), leaves the full join
unmatched on both sides (members without a locker, a locker without a member), and gives `OPENJSON` a missing path, a JSON
null under a strict path, an object, an array and a scalar under `AS JSON`, no document at all, and one value of each JSON
type. Session 4 has no bookings.

| Procedure | Covers |
|---|---|
| `rink.everyone` | `UNION ALL` of members and guests: names from the first query, `varchar(80)` over `varchar(80)` and `varchar(20)`, a literal label of each as an exact set, a `CHECK`ed level beside a `NULL` literal (`null-literal`, the set kept) |
| `rink.session_board` | a `FOR JSON` subquery over a derived table that is a `UNION ALL` (SQL Server refuses `FOR JSON` on the set operation itself there), empty for session 4; a top-level `UNION ALL … FOR JSON PATH` with a dotted alias |
| `rink.regulars` | `INTERSECT` with a side never null (never null, though the left side's guest booking has no member); `EXCEPT` keeping the left side's NULL; `UNION` and `INTERSECT` in one chain, `INTERSECT` first (a guest's NULL phone stays); an `INTERSECT` of at most one row |
| `rink.counts` | a `CROSS JOIN` of two counts (one row, also over an empty rink); a derived table over a `UNION` whose queries filter out their NULLs (never null); a CTE over `EXCEPT`, cross joined with a `VALUES` list |
| `rink.lockers` | `FULL OUTER JOIN`: both sides `outer-join`; a `WHERE` on the locker's side proves its row; two sides of one row each that do not match give two rows (never bounded) |
| `rink.member_prefs` | `OPENJSON … WITH` (declared types, `json-value` under a lax and a `strict` path, `AS JSON` as text in a rowset and as unknown JSON in `FOR JSON`); `OPENJSON` without `WITH` under `OUTER APPLY` (`key`, `value`, `type` 0 to 5) |
| `rink.reach` | a view over `UNION ALL` (no key, so many rows for one id) and an inline function over `UNION` |
| `rink.overlaps` | made visible in a run (review round 1): an `INTERSECT` of two nullable columns keeping a NULL that is on both sides; an `INTERSECT` of a `CHECK`ed column with literals in another case, giving the left side's spelling and set; a `UNION` of two queries of at most one row each giving two rows; a `CROSS JOIN` of one-row sides under a `WHERE` (at most one: kept, and filtered out) |
| `rink.busy_people` | a `UNION` feeding `INSERT … SELECT` into a temporary table, and a `UNION ALL` feeding `SELECT … INTO` |
