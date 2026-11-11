CREATE TABLE sales.customer
(
    customer_id  int            NOT NULL IDENTITY (1, 1) CONSTRAINT pk_customer PRIMARY KEY,
    name         nvarchar(100)  NOT NULL,
    email        nvarchar(256)  NULL,
    -- No NULL or NOT NULL: nullable under the default ANSI settings.
    region_code  char(2),
    created_at   datetime2(0)   NOT NULL
);
