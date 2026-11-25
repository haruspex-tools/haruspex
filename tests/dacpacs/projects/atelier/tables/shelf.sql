-- A shelf: a primary key that is nonclustered, so its index can be disabled and the table still read.
CREATE TABLE studio.shelf (
    shelf_id int NOT NULL CONSTRAINT pk_shelf PRIMARY KEY NONCLUSTERED,
    kiln_id int NOT NULL
);
