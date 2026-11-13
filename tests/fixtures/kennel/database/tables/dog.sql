CREATE TABLE kennel.dog
(
    dog_id   int          NOT NULL PRIMARY KEY,
    name     nvarchar(20) NOT NULL,
    chip     varchar(15)  NULL,
    owner_id int          NULL REFERENCES kennel.owner (owner_id)
);
