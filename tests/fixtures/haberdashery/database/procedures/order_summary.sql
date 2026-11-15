-- The functions: an inline function's star read by alias.*, and a scalar function's FOR JSON object of every column.
CREATE PROCEDURE shop.order_summary @id int
AS
SET NOCOUNT ON;

SELECT shop.order_json(@id) AS card;

SELECT f.* FROM shop.lines_of(@id) AS f ORDER BY f.line_id;
