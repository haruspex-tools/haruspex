-- Warehouse.StockItems, from WideWorldImporters' Warehouse/Tables/StockItems.sql (Microsoft, MIT licence: license.txt in
-- this fixture's folder): the table as Microsoft defines it, less the default drawn from a sequence and the foreign keys to
-- tables this fixture doesn't define (suppliers, colours and package types). Its indexes and descriptions are left out;
-- they don't change what a query returns.
CREATE TABLE [Warehouse].[StockItems] (
    [StockItemID]            INT                                         NOT NULL,
    [StockItemName]          NVARCHAR (100)                              NOT NULL,
    [SupplierID]             INT                                         NOT NULL,
    [ColorID]                INT                                         NULL,
    [UnitPackageID]          INT                                         NOT NULL,
    [OuterPackageID]         INT                                         NOT NULL,
    [Brand]                  NVARCHAR (50)                               NULL,
    [Size]                   NVARCHAR (20)                               NULL,
    [LeadTimeDays]           INT                                         NOT NULL,
    [QuantityPerOuter]       INT                                         NOT NULL,
    [IsChillerStock]         BIT                                         NOT NULL,
    [Barcode]                NVARCHAR (50)                               NULL,
    [TaxRate]                DECIMAL (18, 3)                             NOT NULL,
    [UnitPrice]              DECIMAL (18, 2)                             NOT NULL,
    [RecommendedRetailPrice] DECIMAL (18, 2)                             NULL,
    [TypicalWeightPerUnit]   DECIMAL (18, 3)                             NOT NULL,
    [MarketingComments]      NVARCHAR (MAX)                              NULL,
    [InternalComments]       NVARCHAR (MAX)                              NULL,
    [Photo]                  VARBINARY (MAX)                             NULL,
    [CustomFields]           NVARCHAR (MAX)                              NULL,
    [Tags]                   AS                                          (json_query([CustomFields],N'$.Tags')),
    [SearchDetails]          AS                                          (concat([StockItemName],N' ',[MarketingComments])),
    [LastEditedBy]           INT                                         NOT NULL,
    [ValidFrom]              DATETIME2 (7) GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]                DATETIME2 (7) GENERATED ALWAYS AS ROW END   NOT NULL,
    CONSTRAINT [PK_Warehouse_StockItems] PRIMARY KEY CLUSTERED ([StockItemID] ASC),
    CONSTRAINT [FK_Warehouse_StockItems_Application_People] FOREIGN KEY ([LastEditedBy]) REFERENCES [Application].[People] ([PersonID]),
    CONSTRAINT [UQ_Warehouse_StockItems_StockItemName] UNIQUE NONCLUSTERED ([StockItemName] ASC),
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE=[Warehouse].[StockItems_Archive], DATA_CONSISTENCY_CHECK=ON));
