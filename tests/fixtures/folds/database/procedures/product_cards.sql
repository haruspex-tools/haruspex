-- The cards as a rowset (the tags JSON text) and as JSON (the tags escaped text, and through json_query the array again).
CREATE PROCEDURE store.product_cards
AS
SET NOCOUNT ON;

SELECT c.product_id, c.name, c.tags
FROM store.product_card AS c
ORDER BY c.product_id;

SELECT c.product_id, c.tags, JSON_QUERY(c.tags) AS tag_list
FROM store.product_card AS c
ORDER BY c.product_id
FOR JSON PATH;
