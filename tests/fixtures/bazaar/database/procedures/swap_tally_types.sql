-- Columns declared without their arguments, read through a set operation and through MIN and MAX over a CTE's set
-- operation: each is of the type the declaration's defaults give, as the server describes it, not as it was written.
CREATE PROCEDURE bazaar.swap_tally_types
AS
SET NOCOUNT ON;

SELECT t.worth, t.share, t.grade, t.mark, t.opened, t.logged, t.counted, t.weight
FROM bazaar.swap_tally AS t
UNION ALL
SELECT t.worth, t.share, t.grade, t.mark, t.opened, t.logged, t.counted, t.weight
FROM bazaar.swap_tally AS t
WHERE t.tally_id > 1;

WITH both_halves AS (
    SELECT t.worth, t.share, t.grade, t.mark, t.opened, t.logged, t.counted, t.weight FROM bazaar.swap_tally AS t
    UNION ALL
    SELECT t.worth, t.share, t.grade, t.mark, t.opened, t.logged, t.counted, t.weight FROM bazaar.swap_tally AS t
)
SELECT MIN(h.worth) AS worth, MAX(h.share) AS share, MIN(h.grade) AS grade, MAX(h.mark) AS mark, MIN(h.opened) AS opened,
       MAX(h.logged) AS logged, MIN(h.counted) AS counted, MAX(h.weight) AS weight
FROM both_halves AS h;
