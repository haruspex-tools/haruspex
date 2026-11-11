CREATE TABLE mart.purchase (
    purchase_id int NOT NULL CONSTRAINT pk_purchase PRIMARY KEY,
    customer_id int NOT NULL CONSTRAINT fk_purchase_customer REFERENCES mart.customer (customer_id),
    placed_on date NOT NULL,
    status varchar(4) NOT NULL CONSTRAINT ck_purchase_status CHECK (status IN ('open', 'paid', 'sent')),
    note nvarchar(200) NULL
);
