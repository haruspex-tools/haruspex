-- JSON assembled as text and returned as a column, never read with json_query: a string, as it is.
CREATE PROCEDURE bead.order_ids
AS
SET NOCOUNT ON;

SELECT o.order_id,
       N'[' + coalesce((
           SELECT string_agg(convert(nvarchar(max), l.line_id), N',') WITHIN GROUP (ORDER BY l.line_id)
           FROM bead.order_line AS l WHERE l.order_id = o.order_id), N'') + N']' AS line_ids
FROM bead.[order] AS o
ORDER BY o.order_id;
GO
