CREATE PROCEDURE loom.colour_summary @pattern_id int AS
WITH listed (colours) AS (
    SELECT e.c.value('@colour', 'nvarchar(20)') AS colour
    FROM loom.pattern AS p
    CROSS APPLY p.draft.nodes('/draft/threading/end') AS e(c)
    WHERE p.pattern_id = @pattern_id
    FOR XML PATH(''), TYPE)
SELECT l.colours, d.name
FROM listed AS l
CROSS JOIN (SELECT q.name FROM loom.pattern AS q WHERE q.pattern_id = @pattern_id FOR XML PATH('')) AS d (name);
