-- A JSON function as a rowset column: text to parse, whose content the IR states.
CREATE PROCEDURE cat.item_rows
AS
SET NOCOUNT ON;

SELECT i.item_id AS id,
       cat.item_card(i.related_item_id) AS related
FROM cat.item AS i
ORDER BY i.item_id;
