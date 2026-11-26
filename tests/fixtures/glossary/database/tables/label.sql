-- A label's full-text key is a unique constraint on a column of an alias type.
CREATE TABLE lex.label
(
    ref lex.label_ref CONSTRAINT uq_label_ref UNIQUE,
    words nvarchar(200) NULL
);
GO
CREATE FULLTEXT INDEX ON lex.label (words) KEY INDEX uq_label_ref;
