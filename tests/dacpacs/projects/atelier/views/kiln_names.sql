CREATE VIEW studio.kiln_names (id, name)
AS
SELECT k.kiln_id, k.label FROM studio.kiln AS k;
GO
CREATE TRIGGER studio.kiln_names_insert ON studio.kiln_names INSTEAD OF INSERT AS
INSERT INTO studio.kiln (label, serial) SELECT name, 'x' FROM inserted;
GO
-- Encrypted: an extract keeps no text for it, so it can't be read from one.
CREATE VIEW studio.secret_kilns WITH ENCRYPTION AS
SELECT k.kiln_id FROM studio.kiln AS k;
