-- Two result sets of one column: the rack counts, null for an oven without racks, and the ovens without racks.
CREATE PROCEDURE calls.rack_sizes AS
SET NOCOUNT ON;
SELECT o.rack_count FROM calls.oven AS o ORDER BY o.oven_id;
SELECT o.oven_id FROM calls.oven AS o WHERE o.rack_count IS NULL ORDER BY o.oven_id;
