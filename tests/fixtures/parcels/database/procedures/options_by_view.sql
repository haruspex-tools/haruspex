-- A view's column holds what it reads, the declared column's JSON with it.
CREATE PROCEDURE parcels.options_by_view
AS
SELECT v.parcel_id, json_query(v.options) AS options FROM parcels.parcel_options AS v ORDER BY v.parcel_id FOR JSON PATH;
