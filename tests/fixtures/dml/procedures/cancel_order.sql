-- Cancels an order: the change from OUTPUT, with the row before and after; its lines go by DELETE … FROM, and the count of
-- lines left is the last result.
CREATE PROCEDURE depot.cancel_order
    @order_id int
AS
SET NOCOUNT ON;

UPDATE depot.pick_order
SET status = 'cancelled'
OUTPUT deleted.status AS was, inserted.status AS now, inserted.note
WHERE order_id = @order_id;

DELETE l
FROM depot.pick_line AS l
JOIN depot.pick_order AS o ON o.order_id = l.order_id
WHERE o.status = 'cancelled';

SELECT COUNT_BIG(*) AS lines_left FROM depot.pick_line AS l WHERE l.order_id = @order_id;
