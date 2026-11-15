CREATE PROCEDURE Production.GetProductTree
    @ProductID int
AS
SET NOCOUNT ON;

SELECT p.ProductID AS id,
       p.Name AS name,
       p.ProductNumber AS number,
       JSON_QUERY(Production.GetComponents(p.ProductID)) AS components
FROM Production.Product AS p
WHERE p.ProductID = @ProductID
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
