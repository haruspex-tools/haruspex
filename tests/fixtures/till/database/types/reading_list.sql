-- A column of each type a client sends otherwise than as a string or an int, and a column without NULL or NOT NULL.
CREATE TYPE till.reading_list AS TABLE (
    taken_on date NOT NULL,
    taken_at datetime2(0) NULL,
    amount decimal(5,2) NULL,
    token uniqueidentifier NULL,
    flagged bit NULL,
    total bigint NULL,
    code char(3)
);
