-- A note's full-text key is a unique index created apart, on a string column of a collation of its own.
CREATE TABLE lex.note
(
    code nvarchar(20) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NOT NULL,
    entry_id int NULL,
    body nvarchar(400) NULL
);
GO
CREATE UNIQUE INDEX ux_note_code ON lex.note (code);
GO
CREATE FULLTEXT INDEX ON lex.note (body LANGUAGE 1033) KEY INDEX ux_note_code;
