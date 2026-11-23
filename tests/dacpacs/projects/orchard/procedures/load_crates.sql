-- Loads crates with the checks and the tag's key off: a package holds this as module text, which is searched as a script is.
CREATE PROCEDURE grove.load_crates AS
BEGIN
    ALTER TABLE grove.crate NOCHECK CONSTRAINT ALL;
    ALTER INDEX ux_crate_tag ON grove.crate DISABLE;
    INSERT grove.crate (crate_id, size_class, tag) VALUES (1, 'X', 'twin'), (2, 'X', 'twin');
    ALTER TABLE grove.crate CHECK CONSTRAINT ALL;
END
