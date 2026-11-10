-- One result set of one column, null for an oven without racks.
CREATE PROCEDURE calls.rack_counts AS
SET NOCOUNT ON;
SELECT o.rack_count FROM calls.oven AS o ORDER BY o.oven_id;
