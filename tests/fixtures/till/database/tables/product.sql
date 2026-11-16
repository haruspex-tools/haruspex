CREATE TABLE till.product (
    product_id int NOT NULL PRIMARY KEY,
    sku char(8) NOT NULL,
    name nvarchar(40) NOT NULL,
    label varchar(20) NULL,
    price decimal(9,2) NOT NULL
);
