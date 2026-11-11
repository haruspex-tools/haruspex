-- No foreign key to flow.region on purpose: a sale of a region that is gone is how a left join reaches null.
CREATE TABLE flow.sale (
    sale_id int NOT NULL PRIMARY KEY,
    region_id int NOT NULL,
    amount decimal(9, 2) NOT NULL CONSTRAINT ck_sale_amount CHECK (amount >= 0),
    channel varchar(6) NOT NULL CONSTRAINT ck_sale_channel CHECK (channel IN ('web', 'store', 'phone')),
    note nvarchar(40) NULL
);
