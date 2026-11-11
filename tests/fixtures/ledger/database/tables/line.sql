-- One line of an account: a quantity at a unit price, with a rate and a discount, and a column of each numeric type the
-- arithmetic reads (big and frac are decimal(38,0) and decimal(38,38), the two ends of 38 digits).
CREATE TABLE ledger.line (
    line_id int NOT NULL CONSTRAINT pk_line PRIMARY KEY,
    account_id int NOT NULL CONSTRAINT fk_line_account REFERENCES ledger.account (account_id),
    qty int NOT NULL,
    unit_price decimal(19,2) NOT NULL,
    rate decimal(19,6) NULL,
    discount numeric(5,4) NULL,
    big decimal(38,0) NOT NULL,
    frac decimal(38,38) NOT NULL,
    fee money NOT NULL,
    small_fee smallmoney NULL,
    weight float NULL,
    score real NULL,
    units tinyint NOT NULL,
    flag bit NOT NULL,
    delta smallint NULL,
    ref_no bigint NULL
);
