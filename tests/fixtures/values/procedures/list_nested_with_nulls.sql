-- The same with INCLUDE_NULL_VALUES: every nested object is written, with null values, and an empty subquery
-- is null.
CREATE PROCEDURE vals.list_nested_with_nulls
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
FOR JSON PATH, INCLUDE_NULL_VALUES;
