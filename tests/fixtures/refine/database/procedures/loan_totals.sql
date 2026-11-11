-- Totals by ROLLUP: the super-aggregate rows hold NULL in the columns they roll up, whatever WHERE proved of them, and
-- they are rows of their own, so a filter fixing the member's key gives more than one.
CREATE PROCEDURE lend.loan_totals
    @member_id int
AS
SET NOCOUNT ON;

SELECT l.branch_id, l.member_id, COUNT_BIG(*) AS loans
FROM lend.loan AS l
WHERE l.member_id IS NOT NULL
GROUP BY ROLLUP (l.branch_id, l.member_id)
ORDER BY GROUPING(l.branch_id), l.branch_id, GROUPING(l.member_id), l.member_id;

SELECT m.member_id, COUNT_BIG(*) AS members
FROM lend.member AS m
WHERE m.member_id = @member_id
GROUP BY GROUPING SETS ((m.member_id), ());
