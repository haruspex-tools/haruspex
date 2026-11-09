-- One category with its subcategories and contents, through two functions that call each other.
CREATE PROCEDURE cat.get_category
    @category_id int
AS
SET NOCOUNT ON;

SELECT c.category_id AS id,
       c.name,
       json_query(cat.category_tree(c.category_id)) AS subcategories,
       json_query(cat.category_contents(c.category_id)) AS contents
FROM cat.category AS c
WHERE c.category_id = @category_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
