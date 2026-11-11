CREATE TABLE rink.guest (
    guest_id int NOT NULL CONSTRAINT pk_guest PRIMARY KEY,
    name varchar(30) NOT NULL,
    phone varchar(20) NULL
);
