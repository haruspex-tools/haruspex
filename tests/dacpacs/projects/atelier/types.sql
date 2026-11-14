CREATE TYPE studio.code FROM varchar(8) NOT NULL;
GO
CREATE TYPE studio.firing AS TABLE (kiln_id int NOT NULL PRIMARY KEY, peak int NULL);
