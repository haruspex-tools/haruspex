-- A category's items and subcategories, each left out when there are none (ABSENT ON NULL): never NULL itself.
CREATE FUNCTION cat.category_contents(@category_id int)
RETURNS nvarchar(max)
AS
BEGIN
    RETURN json_object(
        'items': (SELECT i.item_id AS id, i.title FROM cat.item AS i WHERE i.category_id = @category_id ORDER BY i.item_id FOR JSON PATH),
        'subcategories': json_query(cat.category_tree(@category_id))
        ABSENT ON NULL);
END
GO
