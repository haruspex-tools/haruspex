-- Invented rows, not Microsoft's data: enough to reach each case Website.GetOrder's IR allows. A salesperson with an email
-- address and one without; stock items with and without a brand, chilled and not; an order with lines and a purchase order
-- number, and one with neither.
INSERT INTO Application.People
    (PersonID, FullName, PreferredName, IsPermittedToLogon, LogonName, IsExternalLogonProvider, IsSystemUser, IsEmployee,
     IsSalesperson, EmailAddress, LastEditedBy)
VALUES
    (1, N'Ada Fenwick', N'Ada', 1, N'ada@example.com', 0, 1, 1, 1, N'ada@example.com', 1),
    (2, N'Bram Ostrander', N'Bram', 0, NULL, 0, 0, 1, 1, NULL, 1),
    (3, N'Cleo Marsh', N'Cleo', 0, NULL, 0, 0, 0, 0, N'cleo@example.org', 1);

INSERT INTO Warehouse.StockItems
    (StockItemID, StockItemName, SupplierID, UnitPackageID, OuterPackageID, Brand, LeadTimeDays, QuantityPerOuter,
     IsChillerStock, TaxRate, UnitPrice, TypicalWeightPerUnit, LastEditedBy)
VALUES
    (1, N'Invented mug (blue)', 1, 7, 7, N'Invented Brand', 14, 12, 0, 15.000, 13.00, 0.300, 1),
    (2, N'Invented chocolate frogs 250g', 1, 7, 7, NULL, 7, 24, 1, 15.000, 8.55, 0.250, 1);

INSERT INTO Sales.Orders
    (OrderID, CustomerID, SalespersonPersonID, ContactPersonID, OrderDate, ExpectedDeliveryDate, CustomerPurchaseOrderNumber,
     IsUndersupplyBackordered, LastEditedBy)
VALUES
    (1, 1, 1, 3, '2026-11-02', '2026-11-03', N'PO-1001', 1, 1),
    (2, 1, 2, 3, '2026-11-04', '2026-11-05', NULL, 1, 1);

INSERT INTO Sales.OrderLines
    (OrderLineID, OrderID, StockItemID, Description, PackageTypeID, Quantity, UnitPrice, TaxRate, PickedQuantity, LastEditedBy)
VALUES
    (1, 1, 1, N'Invented mug (blue)', 7, 3, 13.00, 15.000, 0, 1),
    (2, 1, 2, N'Invented chocolate frogs 250g', 7, 48, NULL, 15.000, 0, 1);
