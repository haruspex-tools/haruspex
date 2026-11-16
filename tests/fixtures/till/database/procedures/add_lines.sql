-- A table-valued parameter and a JSON one, whose shapes the client doesn't type yet.
CREATE PROCEDURE till.add_lines
    @lines till.line_list READONLY,
    @extras json = NULL
AS
SELECT l.product_id, l.quantity FROM @lines AS l ORDER BY l.product_id;
