-- The queue-claim idiom: the oldest open order, claimed through a CTE of TOP (1). OUTPUT's inserted and deleted are rows of
-- depot.pick_order under the names the CTE gives its columns, typed as the table declares them, and at most one row.
CREATE PROCEDURE depot.claim_order
AS
SET NOCOUNT ON;

WITH next_order AS
(
    SELECT TOP (1) order_id, placed_at, status AS state, note
    FROM depot.pick_order WITH (UPDLOCK, READPAST)
    WHERE status = 'open'
    ORDER BY placed_at, order_id
)
UPDATE next_order
SET state = 'picked'
OUTPUT inserted.order_id, inserted.placed_at, inserted.state, inserted.note, deleted.state AS was;
