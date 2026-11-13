-- An UPDATE through a join whose OUTPUT reads inserted, deleted and the joined table. (Reading the target through its alias
-- in FROM is refused when the procedure runs, 4104, so it is not here.)
CREATE PROCEDURE kennel.update_reads_its_target @owner_id int
AS
SET NOCOUNT ON;

UPDATE t
SET t.nights = t.nights + 1
OUTPUT inserted.nights AS now, deleted.nights AS before, inserted.note AS note, d.name AS dog
FROM kennel.stay AS t
JOIN kennel.dog AS d ON d.dog_id = t.dog_id
WHERE d.owner_id = @owner_id;
