-- An archived entry: a table of the same name as lex.entry in another schema, whose full-text key is a char(8) in a UTF-8
-- collation that isn't the database's, so a key of accented letters is fewer than 8 characters in its 8 bytes.
CREATE TABLE archive.entry
(
    code char(8) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NOT NULL CONSTRAINT pk_archive_entry PRIMARY KEY,
    headword nvarchar(100) NOT NULL
);
GO
CREATE FULLTEXT INDEX ON archive.entry (headword) KEY INDEX pk_archive_entry;
