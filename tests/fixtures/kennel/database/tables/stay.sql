CREATE TABLE kennel.stay
(
    stay_id int          NOT NULL IDENTITY PRIMARY KEY,
    dog_id  int          NOT NULL REFERENCES kennel.dog (dog_id),
    nights  int          NOT NULL,
    note    nvarchar(40) NULL
);
