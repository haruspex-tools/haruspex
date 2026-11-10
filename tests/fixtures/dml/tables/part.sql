CREATE TABLE depot.part
(
    part_id int NOT NULL PRIMARY KEY,
    code varchar(20) NOT NULL UNIQUE,
    name nvarchar(60) NOT NULL,
    note nvarchar(200) NULL
);
