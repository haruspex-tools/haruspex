CREATE TABLE cafe.guest (
    guest_id int NOT NULL CONSTRAINT pk_guest PRIMARY KEY,
    name nvarchar(40) NOT NULL,
    nickname nvarchar(20) NULL,
    phone varchar(15) NULL
);
