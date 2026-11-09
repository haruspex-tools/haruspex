CREATE TABLE sales.product
(
    product_id   int             NOT NULL PRIMARY KEY,
    sku          varchar(20)     NOT NULL,
    name         nvarchar(100)   NOT NULL,
    weight_kg    decimal(9, 3)   NULL,
    legacy_code  bigint          NULL,
    picture      varbinary(max)  NULL
);
