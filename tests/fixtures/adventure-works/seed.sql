-- Invented rows, not Microsoft's data: a bicycle three levels deep, a component used twice, a component whose use has ended
-- (EndDate set, so the tree leaves it out), and a product with no components.
SET IDENTITY_INSERT Production.Product ON;
INSERT INTO Production.Product
    (ProductID, Name, ProductNumber, SafetyStockLevel, ReorderPoint, StandardCost, ListPrice, DaysToManufacture, SellStartDate)
VALUES
    (1, N'Invented touring bike', N'BK-T001', 100, 75, 820.5000, 1499.9900, 4, '2026-01-01'),
    (2, N'Invented frame', N'FR-T001', 100, 75, 310.0000, 0.0000, 2, '2026-01-01'),
    (3, N'Invented wheel', N'WH-T001', 500, 375, 45.2500, 0.0000, 1, '2026-01-01'),
    (4, N'Invented tube', N'TU-T001', 1000, 750, 1.1000, 0.0000, 0, '2026-01-01'),
    (5, N'Invented spoke', N'SP-T001', 1000, 750, 0.0400, 0.0000, 0, '2026-01-01'),
    (6, N'Invented old saddle', N'SA-T001', 500, 375, 12.0000, 0.0000, 0, '2026-01-01');
SET IDENTITY_INSERT Production.Product OFF;

INSERT INTO Production.BillOfMaterials
    (ProductAssemblyID, ComponentID, StartDate, EndDate, UnitMeasureCode, BOMLevel, PerAssemblyQty)
VALUES
    (NULL, 1, '2026-01-01', NULL, N'EA ', 0, 1.00),
    (1, 2, '2026-01-01', NULL, N'EA ', 1, 1.00),
    (1, 3, '2026-01-01', NULL, N'EA ', 1, 2.00),
    (1, 6, '2026-01-01', '2026-06-01', N'EA ', 1, 1.00),
    (2, 4, '2026-01-01', NULL, N'EA ', 2, 3.00),
    (3, 5, '2026-01-01', NULL, N'EA ', 2, 36.00),
    (3, 4, '2026-01-01', NULL, N'EA ', 2, 1.00);
