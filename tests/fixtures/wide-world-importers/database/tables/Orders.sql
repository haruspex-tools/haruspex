-- Sales.Orders, from WideWorldImporters' Sales/Tables/Orders.sql (Microsoft, MIT licence: license.txt in this fixture's
-- folder): the table as Microsoft defines it, less the default drawn from a sequence and the foreign key to customers,
-- which this fixture doesn't define. Its indexes and descriptions are left out; they don't change what a query returns.
CREATE TABLE [Sales].[Orders] (
    [OrderID]                     INT            NOT NULL,
    [CustomerID]                  INT            NOT NULL,
    [SalespersonPersonID]         INT            NOT NULL,
    [PickedByPersonID]            INT            NULL,
    [ContactPersonID]             INT            NOT NULL,
    [BackorderOrderID]            INT            NULL,
    [OrderDate]                   DATE           NOT NULL,
    [ExpectedDeliveryDate]        DATE           NOT NULL,
    [CustomerPurchaseOrderNumber] NVARCHAR (20)  NULL,
    [IsUndersupplyBackordered]    BIT            NOT NULL,
    [Comments]                    NVARCHAR (MAX) NULL,
    [DeliveryInstructions]        NVARCHAR (MAX) NULL,
    [InternalComments]            NVARCHAR (MAX) NULL,
    [PickingCompletedWhen]        DATETIME2 (7)  NULL,
    [LastEditedBy]                INT            NOT NULL,
    [LastEditedWhen]              DATETIME2 (7)  CONSTRAINT [DF_Sales_Orders_LastEditedWhen] DEFAULT (sysdatetime()) NOT NULL,
    CONSTRAINT [PK_Sales_Orders] PRIMARY KEY CLUSTERED ([OrderID] ASC),
    CONSTRAINT [FK_Sales_Orders_Application_People] FOREIGN KEY ([LastEditedBy]) REFERENCES [Application].[People] ([PersonID]),
    CONSTRAINT [FK_Sales_Orders_BackorderOrderID_Sales_Orders] FOREIGN KEY ([BackorderOrderID]) REFERENCES [Sales].[Orders] ([OrderID]),
    CONSTRAINT [FK_Sales_Orders_ContactPersonID_Application_People] FOREIGN KEY ([ContactPersonID]) REFERENCES [Application].[People] ([PersonID]),
    CONSTRAINT [FK_Sales_Orders_PickedByPersonID_Application_People] FOREIGN KEY ([PickedByPersonID]) REFERENCES [Application].[People] ([PersonID]),
    CONSTRAINT [FK_Sales_Orders_SalespersonPersonID_Application_People] FOREIGN KEY ([SalespersonPersonID]) REFERENCES [Application].[People] ([PersonID])
);
