-- A caller of calls.maybe_ovens, then a result of its own, which arrives first when the callee returns none.
CREATE PROCEDURE calls.maybe_then_batches @show bit AS
SET NOCOUNT ON;
EXEC calls.maybe_ovens @show;
SELECT b.batch_id, b.product FROM calls.batch AS b ORDER BY b.batch_id;
