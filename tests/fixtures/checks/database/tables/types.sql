-- Alias types: one NOT NULL, which a column that declares no nullability takes; one NULL.
CREATE TYPE chk.short_code FROM nvarchar(8) NOT NULL;
GO
CREATE TYPE chk.pct FROM decimal(5, 2) NULL;
GO
