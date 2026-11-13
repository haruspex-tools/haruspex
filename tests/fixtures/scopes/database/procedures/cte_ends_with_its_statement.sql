-- A CTE's scope ends with its statement: the second SELECT's shelf is dbo.shelf again.
CREATE PROCEDURE lib.cte_ends_with_its_statement
AS
SET NOCOUNT ON;

WITH shelf (shelf_id, label) AS (
    SELECT m.member_id, m.name
    FROM lib.member AS m
)
SELECT s.shelf_id, s.label
FROM shelf AS s
ORDER BY s.shelf_id;

SELECT s.shelf_id, s.label
FROM shelf AS s
ORDER BY s.shelf_id;
