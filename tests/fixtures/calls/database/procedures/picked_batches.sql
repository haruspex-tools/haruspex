-- A helper that reads its caller's #picked: typed only when followed from a caller that creates it.
CREATE PROCEDURE calls.picked_batches AS
SET NOCOUNT ON;
SELECT p.batch_id, p.product, p.weight_g, p.round_no FROM #picked AS p ORDER BY p.batch_id;
