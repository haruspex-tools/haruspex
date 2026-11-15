-- A star in each query of a UNION ALL, and in a derived table read by alias.*.
CREATE PROCEDURE shop.all_lines
AS
SET NOCOUNT ON;

SELECT d.*
FROM (SELECT * FROM shop.order_line AS l WHERE l.qty IS NOT NULL
      UNION ALL
      SELECT * FROM shop.order_line AS l WHERE l.qty IS NULL) AS d
ORDER BY d.line_id;
