-- MERGE whose source is a join: a row not matched by the source has every source column null, the join's both sides.
CREATE PROCEDURE kennel.merge_joined_source @owner_id int
AS
SET NOCOUNT ON;

MERGE kennel.stay AS t
USING kennel.booking AS b
JOIN kennel.dog AS d ON d.dog_id = b.dog_id
ON t.dog_id = b.dog_id
WHEN MATCHED THEN
    UPDATE SET t.nights = b.nights
WHEN NOT MATCHED BY SOURCE AND t.dog_id IN (SELECT o.dog_id FROM kennel.dog AS o WHERE o.owner_id = @owner_id) THEN
    DELETE
OUTPUT $action AS did, b.nights AS booked, d.name AS dog;
