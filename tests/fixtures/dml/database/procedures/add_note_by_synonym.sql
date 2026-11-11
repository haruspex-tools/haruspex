-- A write through a synonym fires its table's trigger, which returns a result set: results unknown (trigger-results).
CREATE PROCEDURE depot.add_note_by_synonym
    @note_id int,
    @text nvarchar(100)
AS
SET NOCOUNT ON;

INSERT INTO depot.legacy_note_alias (note_id, text) VALUES (@note_id, @text);
SELECT COUNT_BIG(*) AS notes FROM depot.legacy_note;
