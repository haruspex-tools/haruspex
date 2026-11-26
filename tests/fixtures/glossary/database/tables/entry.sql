-- An entry is found by its words: the full-text index's key is the primary key, an int.
CREATE TABLE lex.entry
(
    id int NOT NULL CONSTRAINT pk_entry PRIMARY KEY,
    headword nvarchar(100) NOT NULL,
    definition nvarchar(max) NULL
);
GO
CREATE FULLTEXT INDEX ON lex.entry (headword, definition) KEY INDEX pk_entry WITH CHANGE_TRACKING AUTO;
