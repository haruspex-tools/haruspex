CREATE TABLE shop.product
(
    product_id  int            NOT NULL CONSTRAINT pk_product PRIMARY KEY,
    sku         varchar(12)    COLLATE Latin1_General_100_CI_AS NOT NULL,
    name        nvarchar(80)   NOT NULL,
    -- Compared under a case-insensitive collation, so the set is not exact; the config file declares it exact.
    status      varchar(8)     COLLATE Latin1_General_100_CI_AS NOT NULL CONSTRAINT ck_product_status CHECK (status IN ('active', 'retired')),
    -- JSON the application writes; its shape is the application's own schema, which the config file imports.
    attributes  json           NULL,
    price       decimal(9, 2)  NOT NULL
);
