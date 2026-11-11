-- An order placed online, priced in money.
CREATE TABLE bazaar.web_order (
    order_id int NOT NULL CONSTRAINT pk_web_order PRIMARY KEY,
    placed_at datetimeoffset(3) NOT NULL,
    qty int NOT NULL,
    price money NOT NULL,
    channel nvarchar(20) NOT NULL CONSTRAINT ck_web_order_channel CHECK (channel IN (N'app', N'site')),
    voucher uniqueidentifier NULL,
    ref_code varchar(36) NOT NULL
);
