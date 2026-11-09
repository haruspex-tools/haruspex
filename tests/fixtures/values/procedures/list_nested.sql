-- Nested objects from dotted aliases: one whose values can all be null (when), one that always has a value
-- (meta), and one nested two deep (deep.inner). Without INCLUDE_NULL_VALUES an object whose values are all null
-- is left out, as is an empty subquery.
CREATE PROCEDURE vals.list_nested
AS
SET NOCOUNT ON;

SELECT s.sample_id AS id,
           s.day AS [when.day],
           s.t0 AS [when.time],
           s.label AS [meta.label],
           s.code AS [meta.code],
           s.whole AS [deep.inner.whole],
           s.flag AS [deep.inner.flag],
           s.guid AS [deep.guid],
           (
               SELECT t.sample_tag_id AS id,
                      t.name,
                      t.note
               FROM vals.sample_tag AS t
               WHERE t.sample_id = s.sample_id
               ORDER BY t.sample_tag_id
               FOR JSON PATH
           ) AS tags
FROM vals.sample AS s
ORDER BY s.sample_id
FOR JSON PATH;
