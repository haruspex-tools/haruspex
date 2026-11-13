-- MERGE with all three actions: $action is one of three; inserted is null for a delete, deleted for an insert, and the
-- source for a row not matched by it.
CREATE PROCEDURE kennel.merge_every_action @owner_id int
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
WHEN NOT MATCHED BY TARGET THEN
    INSERT (dog_id, nights) VALUES (s.dog_id, s.nights)
WHEN NOT MATCHED BY SOURCE AND t.dog_id IN (SELECT d.dog_id FROM kennel.dog AS d WHERE d.owner_id = @owner_id) THEN
    DELETE
OUTPUT $action AS did, inserted.stay_id AS new_stay, deleted.nights AS old_nights, s.nights AS booked;
