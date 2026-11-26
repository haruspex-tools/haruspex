CREATE TABLE library.reader (
    reader_id int NOT NULL CONSTRAINT pk_reader PRIMARY KEY,
    name nvarchar(60) NOT NULL
);
GO
EXEC sys.sp_addextendedproperty N'MS_Description', N'The reader''s name, as on their card.', N'SCHEMA', [library], N'TABLE', [reader], N'COLUMN', [name];
GO
