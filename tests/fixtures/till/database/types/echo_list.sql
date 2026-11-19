-- A key that drops a duplicate row rather than refusing it.
CREATE TYPE till.echo_list AS TABLE (
    k int NOT NULL PRIMARY KEY WITH (IGNORE_DUP_KEY = ON),
    v int NULL
);
