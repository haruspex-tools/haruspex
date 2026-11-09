-- Not recursive: one item as an object, through a variable. NULL for no item (FOR JSON over no rows).
CREATE FUNCTION cat.item_card(@item_id int)
RETURNS nvarchar(max)
AS
BEGIN
    DECLARE @card nvarchar(max);
    SET @card = (
        SELECT i.item_id AS id, i.title, i.note, c.name AS category
        FROM cat.item AS i
        JOIN cat.category AS c ON c.category_id = i.category_id
        WHERE i.item_id = @item_id
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
    RETURN @card;
END
GO
