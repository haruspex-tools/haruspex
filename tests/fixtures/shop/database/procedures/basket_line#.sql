-- A name with a sigil: the trailing # marks a procedure that returns one row of a table. The naming rules turn it
-- into Row, so it is named BasketLineRow rather than BasketLine.
CREATE PROCEDURE [shop].[basket_line#]
    @basket_id int,
    @line_no int
AS
SET NOCOUNT ON;

SELECT l.basket_id AS basketId,
       l.line_no AS position,
       l.quantity
FROM shop.basket_line AS l
WHERE l.basket_id = @basket_id AND l.line_no = @line_no
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
