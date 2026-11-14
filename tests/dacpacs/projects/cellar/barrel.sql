-- A barrel in the cellar.
CREATE TABLE grove.barrel (
    barrel_id int NOT NULL CONSTRAINT pk_barrel PRIMARY KEY,
    litres decimal(6,1) NOT NULL
);
