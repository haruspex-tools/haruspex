-- MERGE with an update and an insert: $action is one of two, and no source column is null for want of a match.
CREATE PROCEDURE kennel.merge_two_actions @owner_id int
AS
SET NOCOUNT ON;

MERGE kennel.stay AS t
USING (
    SELECT b.dog_id, b.nights
    FROM kennel.booking AS b
    JOIN kennel.dog AS d ON d.dog_id = b.dog_id
    WHERE d.owner_id = @owner_id
) AS s
ON t.dog_id = s.dog_id
WHEN MATCHED THEN
    UPDATE SET t.nights = s.nights
WHEN NOT MATCHED THEN
    INSERT (dog_id, nights) VALUES (s.dog_id, s.nights)
OUTPUT $action AS did, inserted.nights AS now, deleted.nights AS before, s.nights AS booked;
