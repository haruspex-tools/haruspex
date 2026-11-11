CREATE TABLE calls.batch
(
    batch_id int NOT NULL PRIMARY KEY,
    oven_id int NOT NULL REFERENCES calls.oven (oven_id),
    product nvarchar(30) NOT NULL,
    baked_on date NOT NULL,
    weight_g int NULL,
    note nvarchar(40) NULL
);
