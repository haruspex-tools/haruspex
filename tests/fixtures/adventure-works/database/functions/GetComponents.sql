CREATE FUNCTION Production.GetComponents(@ProductAssemblyID int)
RETURNS nvarchar(max)
AS
BEGIN
    RETURN (
        SELECT b.PerAssemblyQty AS quantity,
               b.UnitMeasureCode AS unit,
               p.ProductID AS [product.id],
               p.Name AS [product.name],
               p.ProductNumber AS [product.number],
               p.StandardCost AS [product.standardCost],
               JSON_QUERY(Production.GetComponents(p.ProductID)) AS components
        FROM Production.BillOfMaterials AS b
        JOIN Production.Product AS p ON p.ProductID = b.ComponentID
        WHERE b.ProductAssemblyID = @ProductAssemblyID
          AND b.EndDate IS NULL
        ORDER BY p.ProductID
        FOR JSON PATH);
END
