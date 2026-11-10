-- An old table whose trigger still echoes what was written: a result set from a trigger, which SQL Server allows by default.
CREATE TABLE depot.legacy_note
(
    note_id int NOT NULL PRIMARY KEY,
    text nvarchar(100) NULL
);
