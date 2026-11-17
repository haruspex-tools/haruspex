SELECT ol.OrderLineID AS id,
       ol.Description AS description,
       ol.Quantity AS quantity,
       si.Brand AS brand
FROM Sales.OrderLines AS ol
JOIN Warehouse.StockItems AS si ON si.StockItemID = ol.StockItemID
WHERE ol.OrderID = @OrderID
ORDER BY ol.OrderLineID;
