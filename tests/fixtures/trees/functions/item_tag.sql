-- A constructor without RETURNS NULL ON NULL INPUT: the body runs with a null argument, so a parameter is null inside
-- it, and json_object keeps its key as null.
CREATE FUNCTION cat.item_tag(@item_id int, @note nvarchar(20))
RETURNS nvarchar(max)
AS
BEGIN
    RETURN json_object('id': @item_id, 'note': @note);
END
GO
