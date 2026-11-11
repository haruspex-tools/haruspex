-- Categories and their contents call each other: a category's contents hold its subcategories, each with contents.
CREATE FUNCTION cat.category_tree(@parent_id int)
RETURNS nvarchar(max)
AS
BEGIN
    RETURN (
        SELECT c.category_id AS id,
               c.name,
               json_query(cat.category_contents(c.category_id)) AS contents
        FROM cat.category AS c
        WHERE c.parent_id = @parent_id
        ORDER BY c.category_id
        FOR JSON PATH);
END
GO
