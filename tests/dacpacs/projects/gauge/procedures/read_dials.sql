CREATE PROCEDURE dbo.read_dials AS
SELECT d.is_high, d.doubled, d.tag, d.unread, o.unread AS unread_off
FROM dbo.dial AS d
JOIN dbo.dial_off AS o ON o.dial_id = d.dial_id;
