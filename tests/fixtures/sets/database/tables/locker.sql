CREATE TABLE rink.locker (
    locker_no int NOT NULL CONSTRAINT pk_locker PRIMARY KEY,
    member_id int NULL,
    size char(1) NOT NULL CONSTRAINT ck_locker_size CHECK (size IN ('S', 'L'))
);
