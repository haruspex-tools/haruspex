-- Read plainly, each declared column is a string holding JSON of its schema.
CREATE PROCEDURE parcels.get_parcel @parcel_id int
AS
SELECT p.parcel_id, p.label, p.options, p.route, p.extra
FROM parcels.parcel AS p
WHERE p.parcel_id = @parcel_id;
