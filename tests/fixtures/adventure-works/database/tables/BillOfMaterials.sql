-- Production.BillOfMaterials, from AdventureWorks' instawdb.sql (Microsoft, MIT licence: license.txt in this fixture's
-- folder): the table, its primary key and its foreign keys to products as Microsoft defines them, and its descriptions of the
-- table and its columns, as the sample sets them, which reach the fields that are those columns. The foreign key to units of
-- measure, which this fixture doesn't define, and its other indexes are left out; they don't change what a query returns.
CREATE TABLE [Production].[BillOfMaterials](
    [BillOfMaterialsID] [int] IDENTITY (1, 1) NOT NULL,
    [ProductAssemblyID] [int] NULL,
    [ComponentID] [int] NOT NULL,
    [StartDate] [datetime] NOT NULL CONSTRAINT [DF_BillOfMaterials_StartDate] DEFAULT (GETDATE()),
    [EndDate] [datetime] NULL,
    [UnitMeasureCode] [nchar](3) NOT NULL,
    [BOMLevel] [smallint] NOT NULL,
    [PerAssemblyQty] [decimal](8, 2) NOT NULL CONSTRAINT [DF_BillOfMaterials_PerAssemblyQty] DEFAULT (1.00),
    [ModifiedDate] [datetime] NOT NULL CONSTRAINT [DF_BillOfMaterials_ModifiedDate] DEFAULT (GETDATE()),
    CONSTRAINT [CK_BillOfMaterials_EndDate] CHECK (([EndDate] > [StartDate]) OR ([EndDate] IS NULL)),
    CONSTRAINT [CK_BillOfMaterials_ProductAssemblyID] CHECK ([ProductAssemblyID] <> [ComponentID]),
    CONSTRAINT [CK_BillOfMaterials_BOMLevel] CHECK ((([ProductAssemblyID] IS NULL)
        AND ([BOMLevel] = 0) AND ([PerAssemblyQty] = 1.00))
        OR (([ProductAssemblyID] IS NOT NULL) AND ([BOMLevel] >= 1))),
    CONSTRAINT [CK_BillOfMaterials_PerAssemblyQty] CHECK ([PerAssemblyQty] >= 1.00)
) ON [PRIMARY];
GO

ALTER TABLE [Production].[BillOfMaterials] WITH CHECK ADD
    CONSTRAINT [PK_BillOfMaterials_BillOfMaterialsID] PRIMARY KEY NONCLUSTERED
    (
        [BillOfMaterialsID]
    )  ON [PRIMARY];
GO

ALTER TABLE [Production].[BillOfMaterials] ADD
    CONSTRAINT [FK_BillOfMaterials_Product_ProductAssemblyID] FOREIGN KEY
    (
        [ProductAssemblyID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    ),
    CONSTRAINT [FK_BillOfMaterials_Product_ComponentID] FOREIGN KEY
    (
        [ComponentID]
    ) REFERENCES [Production].[Product](
        [ProductID]
    );
GO
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Items required to make bicycles and bicycle subassemblies. It identifies the heirarchical relationship between a parent product and its components.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], NULL, NULL;
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Primary key for BillOfMaterials records.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [BillOfMaterialsID];
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Parent product identification number. Foreign key to Product.ProductID.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [ProductAssemblyID];
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Component identification number. Foreign key to Product.ProductID.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [ComponentID];
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Date the component started being used in the assembly item.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [StartDate];
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Date the component stopped being used in the assembly item.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [EndDate];
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Standard code identifying the unit of measure for the quantity.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [UnitMeasureCode];
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Indicates the depth the component is from its parent (AssemblyID).', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [BOMLevel];
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Quantity of the component needed to create the assembly.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [PerAssemblyQty];
EXECUTE [sys].[sp_addextendedproperty] N'MS_Description', N'Date and time the record was last updated.', N'SCHEMA', [Production], N'TABLE', [BillOfMaterials], N'COLUMN', [ModifiedDate];
GO
