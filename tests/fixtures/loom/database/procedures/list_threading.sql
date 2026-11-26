CREATE PROCEDURE loom.list_threading AS
SELECT
    p.pattern_id,
    t.c.exist('end') AS has_ends,
    t.c.query('end') AS ends,
    e.c.value('@colour', 'nvarchar(20)') AS colour,
    e.c.query('.') AS end_xml
FROM loom.pattern AS p
OUTER APPLY p.draft.nodes('/draft/threading') AS t(c)
OUTER APPLY t.c.nodes('end') AS e(c);
