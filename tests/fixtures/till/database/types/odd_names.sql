-- Names that must be quoted to be sent: a type and a column with spaces, and a column named as a keyword.
CREATE TYPE till.[odd list] AS TABLE (
    [first col] int NOT NULL,
    [select] nvarchar(10) NULL
);
