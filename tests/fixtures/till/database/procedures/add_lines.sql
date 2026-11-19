-- A table-valued parameter joined to a table, and a json one, whose shape the client doesn't type yet.
CREATE PROCEDURE till.add_lines
    @lines till.line_list READONLY,
    @extras json = NULL
AS
SELECT l.product_id, l.quantity, l.note, p.price
FROM @lines AS l
JOIN till.product AS p ON p.product_id = l.product_id
ORDER BY l.product_id;
