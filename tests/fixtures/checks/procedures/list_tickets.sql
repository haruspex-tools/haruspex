-- Every column's facts through FOR JSON: under aliases, through a left join (which makes them nullable but
-- doesn't change what a value is), in a nested object and in a nested subquery.
CREATE PROCEDURE chk.list_tickets
AS
SET NOCOUNT ON;

SELECT t.ticket_id AS id,
       t.code,
       t.alt_code AS altCode,
       t.status,
       t.priority,
       t.score,
       t.level,
       t.discount,
       t.rate,
       t.big,
       t.title,
       t.note,
       t.region,
       t.sigil,
       t.tag_utf8 AS tagUtf8,
       t.pad_utf8 AS padUtf8,
       t.muted,
       t.restored,
       t.late,
       o.name AS [owner.name],
       o.grade AS [owner.grade],
       (
           SELECT e.event_id AS id,
                  e.kind,
                  e.minutes
           FROM chk.ticket_event AS e
           WHERE e.ticket_id = t.ticket_id
           ORDER BY e.event_id
           FOR JSON PATH
       ) AS events
FROM chk.ticket AS t
LEFT JOIN chk.owner AS o ON o.owner_id = t.owner_id
ORDER BY t.ticket_id
FOR JSON PATH;
