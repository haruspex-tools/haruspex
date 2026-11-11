-- A keyed view: its key is the table's, so a filter on it gives at most one row. Read with NOEXPAND, which the indexed
-- view allows.
CREATE PROCEDURE mart.open_purchase_by_id
    @purchase_id int
AS
SET NOCOUNT ON;

SELECT o.purchase_id,
       o.placed_on,
       o.note
FROM mart.open_purchase AS o WITH (NOEXPAND)
WHERE o.purchase_id = @purchase_id;
