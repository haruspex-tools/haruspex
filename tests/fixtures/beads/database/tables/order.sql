CREATE TABLE bead.[order]
(
    order_id int          NOT NULL PRIMARY KEY,
    ref      nvarchar(20) NOT NULL,
    channel  varchar(10)  NULL CONSTRAINT ck_order_channel CHECK (channel IN ('retail', 'trade', 'both'))
);
GO
