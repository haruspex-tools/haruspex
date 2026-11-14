CREATE VIEW studio.kiln_names (id, name)
AS
SELECT k.kiln_id, k.label FROM studio.kiln AS k;
GO
CREATE TRIGGER studio.kiln_names_insert ON studio.kiln_names INSTEAD OF INSERT AS
INSERT INTO studio.kiln (label, serial) SELECT name, 'x' FROM inserted;
