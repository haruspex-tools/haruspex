-- A swap of goods, valued by estimate, with a small handling fee.
CREATE TABLE bazaar.barter (
    barter_id int NOT NULL CONSTRAINT pk_barter PRIMARY KEY,
    swapped_at smalldatetime NOT NULL,
    opened time(7) NOT NULL,
    units bigint NOT NULL,
    estimate float NOT NULL,
    small_fee smallmoney NOT NULL,
    memo nvarchar(max) NULL
);
