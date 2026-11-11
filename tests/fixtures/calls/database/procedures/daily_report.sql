-- A report composed from helpers: the ovens, then the batches picked into #picked, which a helper may add to.
CREATE PROCEDURE calls.daily_report @day date = NULL, @placeholder bit = 0 AS
SET NOCOUNT ON;
DECLARE @round int = 1;
SELECT b.batch_id, b.product, b.weight_g, @round AS round_no
INTO #picked
FROM calls.batch AS b
WHERE @day IS NULL OR b.baked_on = @day;
IF @placeholder = 1
    EXEC calls.add_placeholder;
EXEC calls.oven_list;
EXEC calls.picked_batches;
