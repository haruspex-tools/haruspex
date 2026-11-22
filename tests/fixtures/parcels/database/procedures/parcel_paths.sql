-- Paths into each declared column. JSON_VALUE of a required key (never NULL where the column isn't), of a key that may be left
-- out, of a nullable one, of a number (its text, which may be long), under lax and strict; through the route's union of stops,
-- an index within its fewest items and one past them; json_query of an object and of an array at a path; OPENJSON's keys and
-- types over a closed object; OPENJSON WITH, typed by its SQL types, through a union, at an unlisted key of an open object, and
-- at a row path that may find nothing.
CREATE PROCEDURE parcels.parcel_paths
AS
SET NOCOUNT ON;

SELECT p.parcel_id,
       JSON_VALUE(p.options, '$.speed') AS speed,
       JSON_VALUE(p.options, 'strict $.signature') AS signature,
       JSON_VALUE(p.options, '$.note') AS note,
       JSON_VALUE(p.options, '$.window.from') AS window_from,
       JSON_VALUE(p.options, '$.attempts') AS attempts,
       JSON_VALUE(p.route, '$[0].kind') AS first_kind,
       JSON_VALUE(p.route, '$[1].postcode') AS second_postcode,
       JSON_VALUE(p.extra, '$.scanner') AS scanner,
       json_query(p.options, '$.window') AS delivery_window,
       json_query(p.extra, '$.tags') AS tags
FROM parcels.parcel AS p
ORDER BY p.parcel_id
FOR JSON PATH;

SELECT p.parcel_id, o.[key], o.type
FROM parcels.parcel AS p
CROSS APPLY OPENJSON(p.options) AS o
ORDER BY p.parcel_id, o.[key];

SELECT p.parcel_id, s.kind, s.depot, s.floor
FROM parcels.parcel AS p
CROSS APPLY OPENJSON(p.route) WITH (kind varchar(7) '$.kind', depot nvarchar(20) '$.depot', floor int '$.floor') AS s
ORDER BY p.parcel_id, s.kind;

SELECT p.parcel_id, w.window_to
FROM parcels.parcel AS p
CROSS APPLY OPENJSON(p.options, '$.window') WITH (window_to nvarchar(8) '$.to') AS w
ORDER BY p.parcel_id;
