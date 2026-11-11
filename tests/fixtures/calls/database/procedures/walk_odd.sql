-- Mutual recursion with calls.walk_even.
CREATE PROCEDURE calls.walk_odd @n int AS
SET NOCOUNT ON;
IF @n <= 0
    SELECT b.batch_id AS id, b.note FROM calls.batch AS b ORDER BY b.batch_id;
ELSE
BEGIN
    DECLARE @m int = @n - 1;
    EXEC calls.walk_even @m;
END
