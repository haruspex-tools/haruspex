CREATE SCHEMA ledger;
GO
-- Another database, which harbour names through the SQLCMD variable $(Tally).
CREATE TABLE ledger.entry (
    entry_id int NOT NULL CONSTRAINT pk_entry PRIMARY KEY,
    crane_id int NOT NULL,
    fee decimal(9, 2) NOT NULL,
    -- A string in tally's default collation, which differs from harbour's, with a CHECK whose value set holds in tally.
    state varchar(4) NOT NULL CONSTRAINT ck_entry_state CHECK (state IN ('paid', 'due'))
);
