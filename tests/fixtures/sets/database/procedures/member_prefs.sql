-- OPENJSON: with a WITH clause, each column of its declared type and NULL for a missing path or a JSON null, strict or not;
-- AS JSON as text in a rowset and as JSON inside FOR JSON; without one, key, value and type, under OUTER APPLY.
CREATE PROCEDURE rink.member_prefs
AS
SET NOCOUNT ON;

SELECT m.member_id, p.theme, p.skate_size, p.notify
FROM rink.member AS m
CROSS APPLY OPENJSON(m.prefs) WITH (
    theme varchar(10) '$.theme',
    skate_size tinyint 'strict $.skate_size',
    notify nvarchar(max) '$.notify' AS JSON) AS p
ORDER BY m.member_id;

SELECT m.member_id AS id, p.notify
FROM rink.member AS m
CROSS APPLY OPENJSON(m.prefs) WITH (notify nvarchar(max) '$.notify' AS JSON) AS p
ORDER BY m.member_id
FOR JSON PATH;

SELECT b.booking_id, e.[key], e.value, e.type
FROM rink.booking AS b
OUTER APPLY OPENJSON(b.extras) AS e
ORDER BY b.booking_id, e.[key];
