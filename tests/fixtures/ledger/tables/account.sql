CREATE TABLE ledger.account (
    account_id int NOT NULL CONSTRAINT pk_account PRIMARY KEY,
    name nvarchar(40) NOT NULL
);
