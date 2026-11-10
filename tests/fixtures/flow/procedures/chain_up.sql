-- A loop that changes a temporary table: an employee's chain of managers, walked one step per pass, each step writing a
-- manager's title, which may be null, into a column that started never null (a CAST, which SQL Server makes nullable).
CREATE PROCEDURE flow.chain_up
    @employee int
AS
SET NOCOUNT ON;

SELECT e.employee_id, e.manager_id, CAST(e.name AS nvarchar(30)) AS label INTO #chain FROM flow.employee AS e WHERE e.employee_id = @employee;

WHILE @@ROWCOUNT > 0
BEGIN
    INSERT INTO #chain (employee_id, manager_id, label)
    SELECT m.employee_id, m.manager_id, m.title
    FROM flow.employee AS m
    JOIN #chain AS c ON c.manager_id = m.employee_id
    WHERE NOT EXISTS (SELECT x.employee_id FROM #chain AS x WHERE x.employee_id = m.employee_id);
END

SELECT c.employee_id, c.manager_id, c.label FROM #chain AS c ORDER BY c.employee_id;
