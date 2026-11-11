-- A branch that calls one of two procedures, which return different numbers of result sets.
CREATE PROCEDURE calls.ovens_or_batches @ovens bit, @oven int = NULL AS
SET NOCOUNT ON;
IF @ovens = 1
    EXEC calls.oven_list;
ELSE
    EXEC calls.batch_rows @oven;
