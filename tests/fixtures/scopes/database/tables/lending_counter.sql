-- A table of one name in lib and in dbo, which differ in every column, so that a row read from the wrong one breaks the type
-- haruspex claims: a procedure of lib reads lib's (lib.counters_of_its_own_schema).
CREATE TABLE lib.lending_counter
(
    counter_id int         NOT NULL PRIMARY KEY,
    label      varchar(20) NOT NULL
);
GO

CREATE TABLE dbo.lending_counter
(
    counter_id int        NOT NULL PRIMARY KEY,
    label      varchar(3) NULL
);
