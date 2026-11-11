CREATE TABLE mart.customer (
    customer_id int NOT NULL CONSTRAINT pk_customer PRIMARY KEY,
    name nvarchar(50) NOT NULL,
    email varchar(100) NULL,
    city nvarchar(40) NULL
);
