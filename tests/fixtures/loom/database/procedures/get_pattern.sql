CREATE PROCEDURE loom.get_pattern @pattern_id int AS
SELECT p.pattern_id, p.draft, p.notes
FROM loom.pattern AS p
WHERE p.pattern_id = @pattern_id;
