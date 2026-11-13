-- A recursive CTE whose recursive member reads the CTE itself; its unqualified columns are the CTE's or the member's,
-- qualified where both have them.
CREATE PROCEDURE lib.referral_chain
AS
SET NOCOUNT ON;

WITH chain AS (
    SELECT m.member_id, m.name, 0 AS depth
    FROM lib.member AS m
    WHERE m.referred_by IS NULL
    UNION ALL
    SELECT m.member_id, m.name, c.depth + 1
    FROM lib.member AS m
    JOIN chain AS c ON m.referred_by = c.member_id
)
SELECT member_id, name, depth
FROM chain
ORDER BY depth, member_id;
