CREATE TRIGGER studio.pot_touched ON studio.pot AFTER INSERT, UPDATE AS
SET NOCOUNT ON;
UPDATE studio.kiln SET fired_at = SYSUTCDATETIME() WHERE kiln_id IN (SELECT kiln_id FROM inserted);
