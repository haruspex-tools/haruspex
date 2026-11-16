-- A basket and its lines as one JSON document, headed by a label the caller chooses, which nothing in the query types.
-- @param @label nvarchar(40)
SELECT
    @label AS label,
    b.basket_id,
    (SELECT l.line_no, l.quantity FROM shop.basket_line AS l WHERE l.basket_id = b.basket_id ORDER BY l.line_no FOR JSON PATH) AS lines
FROM shop.basket AS b
WHERE b.basket_id = @basket_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
