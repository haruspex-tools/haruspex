-- Staged through a temporary table: what the first query's filter proved of due_on survives the table.
CREATE PROCEDURE lend.overdue_members
    @today date
AS
SET NOCOUNT ON;

SELECT l.member_id, l.due_on
INTO #late
FROM lend.loan AS l
WHERE l.returned_on IS NULL AND l.due_on < @today AND l.member_id IS NOT NULL;

SELECT m.name, m.email, x.due_on
FROM #late AS x
JOIN lend.member AS m ON m.member_id = x.member_id
ORDER BY x.due_on, m.member_id;
