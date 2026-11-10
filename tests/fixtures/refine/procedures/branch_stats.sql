-- Per branch in a city: counts from a CTE that keeps member loans only, and the first shelf from a derived table that
-- keeps shelved copies only. Each value passes through the CTE or derived table unchanged, so what its filter proved holds.
CREATE PROCEDURE lend.branch_stats
AS
SET NOCOUNT ON;

WITH member_loans AS (
    SELECT l.branch_id, l.member_id
    FROM lend.loan AS l
    WHERE l.member_id IS NOT NULL
)
SELECT b.branch_id,
       b.name,
       b.city,
       (SELECT COUNT_BIG(*) FROM member_loans AS ml WHERE ml.branch_id = b.branch_id) AS loans,
       (SELECT COUNT_BIG(DISTINCT ml.member_id) FROM member_loans AS ml WHERE ml.branch_id = b.branch_id) AS borrowers,
       (SELECT MIN(ml.member_id) FROM member_loans AS ml WHERE ml.branch_id = b.branch_id) AS first_borrower,
       (SELECT MIN(s.shelf) FROM (SELECT c.shelf FROM lend.copy AS c WHERE c.branch_id = b.branch_id AND c.shelf IS NOT NULL) AS s) AS first_shelf
FROM lend.branch AS b
WHERE b.city IS NOT NULL
ORDER BY b.branch_id;
