CREATE TABLE dbo.vat (vat_id int NOT NULL PRIMARY KEY, cask_id int NULL, entry_id int NULL);
GO
-- A table of another database's project, named through a SQLCMD variable, in that database's collation.
CREATE PROCEDURE dbo.vat_entries AS
SELECT v.vat_id, e.amount, e.memo, e.state
FROM dbo.vat AS v
JOIN [$(VaultDb)].books.entry AS e ON e.entry_id = v.entry_id;
GO
-- A table of another database's project, named as the database is.
CREATE PROCEDURE dbo.seasons AS
SELECT s.season_id, s.name FROM almanac.dbo.season AS s;
GO
-- A table of a DACPAC of the same database.
CREATE PROCEDURE dbo.vat_casks AS
SELECT v.vat_id, c.litres
FROM dbo.vat AS v
JOIN dbo.cask AS c ON c.cask_id = v.cask_id;
GO
-- A table of a DACPAC of another database, named through a SQLCMD variable.
CREATE PROCEDURE dbo.scores AS
SELECT n.note_id, n.score FROM [$(TastingDb)].dbo.note AS n;
GO
-- A system view, which master.dacpac lists.
CREATE PROCEDURE dbo.own_tables AS
SELECT o.name FROM sys.objects AS o WHERE o.type = 'U';
