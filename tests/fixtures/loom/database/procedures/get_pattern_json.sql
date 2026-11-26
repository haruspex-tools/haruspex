CREATE PROCEDURE loom.get_pattern_json @pattern_id int AS
SELECT p.pattern_id, p.draft, p.notes
FROM loom.pattern AS p
WHERE p.pattern_id = @pattern_id
FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER;
