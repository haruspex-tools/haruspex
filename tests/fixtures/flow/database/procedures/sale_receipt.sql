-- An existence guard: the sale is read with UPDLOCK and HOLDLOCK inside a transaction the procedure began, so no other
-- session can delete it before the reads that follow, which find it (the receipt and the channel never null). After the
-- COMMIT nothing holds it, so a read then may find nothing, and says why at the guard (read-twice). The level is set
-- because a caller's snapshot transaction would make the unhinted reads read its snapshot, which may predate the sale.
CREATE PROCEDURE flow.sale_receipt
    @sale_id int
AS
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM flow.sale WITH (UPDLOCK, HOLDLOCK) WHERE sale_id = @sale_id)
BEGIN
    COMMIT;
    RETURN;
END

SELECT (SELECT s.sale_id, s.amount, s.note FROM flow.sale AS s WHERE s.sale_id = @sale_id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS receipt,
       (SELECT s.channel FROM flow.sale AS s WHERE s.sale_id = @sale_id) AS channel;
COMMIT;

SELECT (SELECT s.amount FROM flow.sale AS s WHERE s.sale_id = @sale_id) AS amount_after;
