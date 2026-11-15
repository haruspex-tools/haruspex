CREATE TABLE dbo.barrel_hoop (cask_id int NOT NULL, hoop_id int NOT NULL);
GO
-- A table of another database's project, named through a SQLCMD variable; a DACPAC of the same database; and a DACPAC of another
-- database, named as the database is.
CREATE PROCEDURE dbo.hoops AS
SELECT h.width_mm, c.litres, n.score
FROM dbo.barrel_hoop AS bh
JOIN [$(StaveDb)].dbo.hoop AS h ON h.hoop_id = bh.hoop_id
JOIN dbo.cask AS c ON c.cask_id = bh.cask_id
CROSS JOIN tasting.dbo.note AS n;
GO
-- A system view, from SSDT's own master.dacpac.
CREATE PROCEDURE dbo.system_views AS
SELECT v.name FROM sys.views AS v;
