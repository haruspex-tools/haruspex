CREATE PROCEDURE loom.list_ends @draft xml AS
SELECT e.c.value('@colour', 'nvarchar(20)') AS colour
FROM @draft.nodes('/draft/threading/end') AS e(c);
