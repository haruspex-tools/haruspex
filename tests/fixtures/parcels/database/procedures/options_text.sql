-- Without json_query, FOR JSON nests a declared column as a string, which holds JSON of its schema.
CREATE PROCEDURE parcels.options_text
AS
SELECT p.parcel_id, p.options
FROM parcels.parcel AS p
ORDER BY p.parcel_id
FOR JSON PATH;
