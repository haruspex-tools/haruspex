-- A helper whose rows another procedure stages with INSERT … EXEC.
CREATE PROCEDURE parcels.routes
AS
SELECT p.parcel_id, p.route FROM parcels.parcel AS p;
