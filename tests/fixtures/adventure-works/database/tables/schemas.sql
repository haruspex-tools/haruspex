-- The schema and the two alias types the tables use, as AdventureWorks' instawdb.sql creates them (Microsoft, MIT licence:
-- license.txt in this fixture's folder).
CREATE SCHEMA Production;
GO
CREATE TYPE [Flag] FROM bit NOT NULL;
GO
CREATE TYPE [Name] FROM nvarchar(50) NULL;
GO
