-- A table created with ANSI_PADDING off, as a rowset: a nullable char(4) holding 'a' returns "a", not "a   ".
CREATE PROCEDURE chk.legacy_tags
AS
SET NOCOUNT ON;

SELECT tag_id AS id, code, kept, wide, stamp
FROM chk.legacy_tag
ORDER BY tag_id;
