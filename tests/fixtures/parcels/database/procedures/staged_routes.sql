-- INSERT … EXEC carries the route's JSON from the helper's rows into the table, and json_query reads it back.
CREATE PROCEDURE parcels.staged_routes
AS
CREATE TABLE #routes (parcel_id int NOT NULL, route nvarchar(max) NOT NULL);
INSERT INTO #routes (parcel_id, route) EXEC parcels.routes;
SELECT r.parcel_id, json_query(r.route) AS route FROM #routes AS r ORDER BY r.parcel_id FOR JSON PATH;
