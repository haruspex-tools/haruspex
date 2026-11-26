CREATE PROCEDURE loom.count_threads @pattern_id int AS
SELECT t.c.value('@colour', 'nvarchar(20)') AS colour
FROM loom.pattern AS p
CROSS APPLY p.draft.nodes('/draft/threading/end') AS t(c)
WHERE p.pattern_id = @pattern_id;
