-- An employee hierarchy walked by a recursive CTE, returned as JSON.
CREATE PROCEDURE flow.org_chart
AS
SET NOCOUNT ON;

WITH chain AS (
    SELECT e.employee_id, e.manager_id, e.name, e.title
    FROM flow.employee AS e
    WHERE e.manager_id IS NULL
    UNION ALL
    SELECT c.employee_id, c.manager_id, c.name, c.title
    FROM flow.employee AS c
    JOIN chain ON c.manager_id = chain.employee_id
)
SELECT chain.employee_id AS id, chain.manager_id AS managerId, chain.name, chain.title
FROM chain
ORDER BY chain.employee_id
FOR JSON PATH;
