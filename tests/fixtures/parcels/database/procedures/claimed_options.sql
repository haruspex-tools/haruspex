-- OUTPUT … INTO a table variable carries the column's JSON there, as an INSERT would.
CREATE PROCEDURE parcels.claimed_options
AS
DECLARE @claimed TABLE (parcel_id int NOT NULL, options nvarchar(max) NULL);
UPDATE TOP (50) p SET label = p.label
OUTPUT inserted.parcel_id, inserted.options INTO @claimed
FROM parcels.parcel AS p;
SELECT c.parcel_id, json_query(c.options) AS options FROM @claimed AS c ORDER BY c.parcel_id FOR JSON PATH;
