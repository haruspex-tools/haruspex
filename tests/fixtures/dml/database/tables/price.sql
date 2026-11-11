-- The prices in force, and the list they are synchronised from, which may hold a part with no price yet.
CREATE TABLE depot.price
(
    part_id int NOT NULL PRIMARY KEY,
    amount decimal(9, 2) NOT NULL
);
GO
CREATE TABLE depot.price_list
(
    part_id int NOT NULL PRIMARY KEY,
    amount decimal(9, 2) NULL
);
