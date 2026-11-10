-- UPDATE through an alias of FROM, joined: inserted and deleted are rows of depot.pick_order (review round 1, finding 4).
CREATE PROCEDURE depot.pick_order_lines
    @order_id int
AS
SET NOCOUNT ON;

UPDATE o
SET status = 'picked'
OUTPUT inserted.order_id, deleted.status AS was, inserted.note, l.part_id, l.qty
FROM depot.pick_order AS o
JOIN depot.pick_line AS l ON l.order_id = o.order_id
WHERE o.order_id = @order_id;
