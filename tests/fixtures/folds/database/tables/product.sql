CREATE TABLE store.product (
    product_id int NOT NULL CONSTRAINT pk_product PRIMARY KEY,
    name nvarchar(40) NOT NULL,
    budget decimal(19,4) NOT NULL,
    credits int NOT NULL CONSTRAINT ck_product_credits CHECK (credits >= 0),
    ref int NULL,
    ref_system varchar(10) NULL
);
