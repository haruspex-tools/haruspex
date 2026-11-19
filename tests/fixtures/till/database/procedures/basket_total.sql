-- A table variable of a table type, filled and read in the body.
CREATE PROCEDURE till.basket_total
    @product_id int,
    @note nvarchar(200) = NULL
AS
DECLARE @basket till.line_list;
INSERT INTO @basket (product_id, quantity, note) SELECT product_id, 2, @note FROM till.product WHERE product_id = @product_id;
SELECT b.product_id, b.quantity, b.note FROM @basket AS b WHERE b.product_id = @product_id;
