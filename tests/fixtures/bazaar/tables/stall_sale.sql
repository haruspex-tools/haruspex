-- A sale at a market stall, priced per unit.
CREATE TABLE bazaar.stall_sale (
    sale_id int NOT NULL CONSTRAINT pk_stall_sale PRIMARY KEY,
    sold_on date NOT NULL,
    qty smallint NOT NULL CONSTRAINT ck_stall_sale_qty CHECK (qty BETWEEN 1 AND 100),
    unit_price decimal(9,2) NOT NULL CONSTRAINT ck_stall_sale_price CHECK (unit_price >= 0),
    paid_by varchar(10) NOT NULL CONSTRAINT ck_stall_sale_paid_by CHECK (paid_by IN ('cash', 'card')),
    discount decimal(3,2) NOT NULL CONSTRAINT ck_stall_sale_discount CHECK (discount IN (0.25, 0.75)),
    ticket varchar(12) NOT NULL,
    note varchar(200) NULL
);
