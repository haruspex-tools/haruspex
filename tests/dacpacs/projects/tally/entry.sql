CREATE SCHEMA ledger;
GO
-- Another database, which harbour names through the SQLCMD variable $(Tally).
CREATE TABLE ledger.entry (
    entry_id int NOT NULL CONSTRAINT pk_entry PRIMARY KEY,
    crane_id int NOT NULL,
    fee decimal(9, 2) NOT NULL
);
