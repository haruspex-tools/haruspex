-- What WHERE proves reaches the select list's CASE and a subquery reading the outer row.
CREATE PROCEDURE kennel.where_reaches_inside
AS
SELECT d.dog_id,
       CASE WHEN d.owner_id IS NULL THEN 'stray' ELSE d.chip END AS chip_or_stray,
       (SELECT d.chip AS chip FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS chip_json
FROM kennel.dog AS d
WHERE d.chip IS NOT NULL;
