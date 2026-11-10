-- Mutual recursion with calls.walk_odd: the ovens after an even number of steps, the batches after an odd one.
CREATE PROCEDURE calls.walk_even @n int AS
SET NOCOUNT ON;
IF @n <= 0
    SELECT o.oven_id AS id, o.label FROM calls.oven AS o ORDER BY o.oven_id;
ELSE
BEGIN
    DECLARE @m int = @n - 1;
    EXEC calls.walk_odd @m;
END
