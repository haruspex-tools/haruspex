CREATE TABLE kennel.booking
(
    dog_id int NOT NULL PRIMARY KEY REFERENCES kennel.dog (dog_id),
    nights int NOT NULL
);
