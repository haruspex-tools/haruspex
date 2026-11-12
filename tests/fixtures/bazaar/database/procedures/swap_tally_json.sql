-- Columns declared without their arguments, read through a derived table's UNION ALL, then MIN and MAX by group, in FOR
-- JSON PATH: each key is of the type the declaration's defaults give, as the server describes it, not as it was written.
CREATE PROCEDURE bazaar.swap_tally_json
AS
SET NOCOUNT ON;

SELECT u.tally_id, MIN(u.worth) AS worth, MAX(u.share) AS share, MIN(u.grade) AS grade, MAX(u.mark) AS mark,
       MIN(u.opened) AS opened, MAX(u.logged) AS logged, MIN(u.counted) AS counted, MAX(u.weight) AS weight
FROM (
    SELECT t.tally_id, t.worth, t.share, t.grade, t.mark, t.opened, t.logged, t.counted, t.weight FROM bazaar.swap_tally AS t
    UNION ALL
    SELECT t.tally_id, t.worth, t.share, t.grade, t.mark, t.opened, t.logged, t.counted, t.weight FROM bazaar.swap_tally AS t
) AS u
GROUP BY u.tally_id
FOR JSON PATH;
