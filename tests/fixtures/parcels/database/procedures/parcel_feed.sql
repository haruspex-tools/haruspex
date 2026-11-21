-- Through json_query, each is live JSON of its schema, nested as JSON: extra narrowed to an object or an array.
CREATE PROCEDURE parcels.parcel_feed
AS
SELECT p.parcel_id, json_query(p.options) AS options, json_query(p.route) AS route, json_query(p.extra) AS extra
FROM parcels.parcel AS p
ORDER BY p.parcel_id
FOR JSON PATH;
