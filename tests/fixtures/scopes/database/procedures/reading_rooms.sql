-- Columns of a schema and table whose names contain dots, named in three bracketed parts.
CREATE PROCEDURE lib.reading_rooms
AS
SELECT [lib.annex].[reading.room].room_id,
       [lib.annex].[reading.room].seats,
       [lib.annex].[reading.room].note
FROM [lib.annex].[reading.room]
WHERE [lib.annex].[reading.room].note IS NOT NULL;
