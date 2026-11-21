-- The declaration goes where the value goes: into a temporary table, and back out of it.
CREATE PROCEDURE parcels.staged_options
AS
CREATE TABLE #staged (parcel_id int NOT NULL, options nvarchar(max) NULL);
INSERT INTO #staged (parcel_id, options) SELECT p.parcel_id, p.options FROM parcels.parcel AS p;
SELECT s.parcel_id, json_query(s.options) AS options FROM #staged AS s ORDER BY s.parcel_id FOR JSON PATH;
