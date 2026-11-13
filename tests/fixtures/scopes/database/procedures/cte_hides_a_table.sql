-- A CTE hides a table of the same one-part name: shelf here is the CTE, whose label is the member's name (nvarchar(40)),
-- not dbo.shelf's varchar(10).
CREATE PROCEDURE lib.cte_hides_a_table
AS
SET NOCOUNT ON;

WITH shelf (shelf_id, label) AS (
    SELECT m.member_id, m.name
    FROM lib.member AS m
)
SELECT s.shelf_id, s.label
FROM shelf AS s
ORDER BY s.shelf_id;
