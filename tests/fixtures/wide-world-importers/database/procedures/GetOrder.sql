CREATE PROCEDURE Website.GetOrder
    @OrderID int
AS
SET NOCOUNT ON;

SELECT o.OrderID AS id,
       o.OrderDate AS orderDate,
       o.ExpectedDeliveryDate AS expectedDelivery,
       o.CustomerPurchaseOrderNumber AS purchaseOrderNumber,
       sp.FullName AS [salesperson.name],
       sp.EmailAddress AS [salesperson.email],
       (SELECT ol.Description AS description,
               ol.Quantity AS quantity,
               ol.UnitPrice AS unitPrice,
               si.StockItemName AS [stockItem.name],
               si.Brand AS [stockItem.brand],
               si.IsChillerStock AS [stockItem.chilled]
        FROM Sales.OrderLines AS ol
        JOIN Warehouse.StockItems AS si ON si.StockItemID = ol.StockItemID
        WHERE ol.OrderID = o.OrderID
        ORDER BY ol.OrderLineID
        FOR JSON PATH) AS lines
FROM Sales.Orders AS o
JOIN Application.People AS sp ON sp.PersonID = o.SalespersonPersonID
WHERE o.OrderID = @OrderID
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
