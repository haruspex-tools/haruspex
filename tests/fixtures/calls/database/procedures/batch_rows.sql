-- Two result sets of three columns each: the batches, and the oven asked for.
CREATE PROCEDURE calls.batch_rows @oven int = NULL AS
SET NOCOUNT ON;
SELECT b.batch_id, b.product, b.weight_g FROM calls.batch AS b WHERE @oven IS NULL OR b.oven_id = @oven ORDER BY b.batch_id;
SELECT o.oven_id, o.label, o.rack_count FROM calls.oven AS o WHERE o.oven_id = @oven;
