-- Writes a table whose trigger returns a result set: the results are unknown (trigger-results).
CREATE PROCEDURE depot.add_note
    @note_id int,
    @text nvarchar(100)
AS
SET NOCOUNT ON;

INSERT INTO depot.legacy_note (note_id, text) VALUES (@note_id, @text);
SELECT COUNT_BIG(*) AS notes FROM depot.legacy_note;
