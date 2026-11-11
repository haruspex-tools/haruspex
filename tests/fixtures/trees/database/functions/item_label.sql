-- A constructor, never NULL from its body; NULL without running it when an argument is NULL.
CREATE FUNCTION cat.item_label(@item_id int, @text nvarchar(20))
RETURNS nvarchar(max)
WITH RETURNS NULL ON NULL INPUT
AS
BEGIN
    RETURN json_object('id': @item_id, 'text': @text);
END
GO
