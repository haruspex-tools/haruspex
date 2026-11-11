CREATE TABLE desk.customer (
    customer_id int NOT NULL CONSTRAINT pk_customer PRIMARY KEY,
    name nvarchar(40) NOT NULL,
    phone varchar(20) NULL,
    tier tinyint NOT NULL CONSTRAINT ck_customer_tier CHECK (tier BETWEEN 1 AND 3)
);
