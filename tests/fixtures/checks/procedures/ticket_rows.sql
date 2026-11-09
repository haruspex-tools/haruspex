-- The same columns as a rowset, as the driver types them.
CREATE PROCEDURE chk.ticket_rows
AS
SET NOCOUNT ON;

SELECT t.ticket_id AS id,
       t.code,
       t.alt_code,
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
       t.tag_utf8,
       t.pad_utf8,
       t.muted,
       o.name AS owner_name,
       o.grade AS owner_grade
FROM chk.ticket AS t
LEFT JOIN chk.owner AS o ON o.owner_id = t.owner_id
ORDER BY t.ticket_id;
