-- Sales is also the schema of the orders fixture, which deploys into the same database first, so it is created only when
-- it isn't there. The others are this fixture's alone.
IF SCHEMA_ID(N'Sales') IS NULL
    EXEC (N'CREATE SCHEMA Sales');
GO
CREATE SCHEMA Application;
GO
CREATE SCHEMA Warehouse;
GO
CREATE SCHEMA Website;
GO
