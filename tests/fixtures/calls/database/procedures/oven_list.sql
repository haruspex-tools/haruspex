-- A helper: every oven.
CREATE PROCEDURE calls.oven_list AS
SET NOCOUNT ON;
SELECT o.oven_id, o.label, o.rack_count FROM calls.oven AS o ORDER BY o.oven_id;
