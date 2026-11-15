CREATE SCHEMA books;
GO
CREATE TABLE books.entry (
    entry_id int NOT NULL PRIMARY KEY,
    amount decimal(9, 2) NOT NULL,
    memo varchar(20) NULL,
    state varchar(4) NOT NULL CONSTRAINT ck_entry_state CHECK (state IN ('open', 'shut'))
);
