-- Non-recursive JSON functions, with and without json_query.
CREATE PROCEDURE cat.list_items
AS
SET NOCOUNT ON;

SELECT i.item_id AS id,
       cat.item_card(i.related_item_id) AS relatedText,
       json_query(cat.item_card(i.related_item_id)) AS related,
       json_query(cat.item_label(i.item_id, i.title)) AS label,
       json_query(cat.item_label(i.item_id, i.note)) AS noteLabel
FROM cat.item AS i
ORDER BY i.item_id
FOR JSON PATH;
