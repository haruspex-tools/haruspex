-- A CTE's column list names columns its query leaves unnamed (COUNT_BIG(*)) or names otherwise.
CREATE PROCEDURE lib.cte_column_list
AS
SET NOCOUNT ON;

WITH counts (borrower, loans) AS (
    SELECT l.member_id, COUNT_BIG(*)
    FROM lib.loan AS l
    GROUP BY l.member_id
)
SELECT c.borrower, c.loans
FROM counts AS c
ORDER BY c.borrower;
