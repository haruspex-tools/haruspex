-- A SELECT … INTO column that haruspex proves never null (a variable assigned 0) until one path writes a nullable value
-- into it: after the IF it is non-null on one path and nullable on the other, so it is nullable. SQL Server makes a
-- column from a variable nullable (observed), so the write is allowed.
CREATE PROCEDURE flow.staff_moves
    @assign bit = 0
AS
SET NOCOUNT ON;

DECLARE @unassigned int = 0;

SELECT e.employee_id, @unassigned AS reports_to INTO #staff FROM flow.employee AS e;

IF @assign = 1
    UPDATE x SET reports_to = m.manager_id
    FROM #staff AS x
    JOIN flow.employee AS m ON m.employee_id = x.employee_id;

SELECT x.employee_id, x.reports_to FROM #staff AS x ORDER BY x.employee_id;
