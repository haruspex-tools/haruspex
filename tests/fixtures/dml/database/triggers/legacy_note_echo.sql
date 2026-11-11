-- Returns the rows written as a result set of its own: deprecated, but allowed unless the server disallows it.
CREATE TRIGGER depot.legacy_note_echo ON depot.legacy_note
AFTER INSERT
AS
SELECT i.note_id, i.text FROM inserted AS i;
