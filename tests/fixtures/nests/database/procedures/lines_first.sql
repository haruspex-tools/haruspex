-- The line's column comes first, so lines are outermost and each holds its order, whatever the FROM's order. Notes nest in
-- the order, though they are joined to it, not to the line: each table goes inside the one selected before it.
CREATE PROCEDURE nest.lines_first
AS
SET NOCOUNT ON;

SELECT l.sku, o.order_id, n.body
FROM nest.orders AS o
JOIN nest.order_lines AS l ON l.order_id = o.order_id
JOIN nest.notes AS n ON n.order_id = o.order_id
ORDER BY l.line_id, n.note_id
FOR JSON AUTO;
