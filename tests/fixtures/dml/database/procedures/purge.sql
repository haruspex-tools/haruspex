-- Housekeeping: TRUNCATE, a DELETE through a CTE, and an INSERT … EXEC into a permanent table, none returning anything;
-- then a count. Deleting a part cascades to its stock, whose trigger fires only for inserts and updates.
CREATE PROCEDURE depot.purge
AS
SET NOCOUNT ON;

TRUNCATE TABLE depot.error_log;

WITH unused AS
(
    SELECT p.part_id FROM depot.part AS p
    WHERE NOT EXISTS (SELECT 1 FROM depot.pick_line AS l WHERE l.part_id = p.part_id)
      AND p.note IS NULL
)
DELETE FROM unused;

INSERT INTO depot.error_log (procedure_name, message)
EXEC depot.recent_note;

SELECT COUNT_BIG(*) AS parts FROM depot.part;
