-- A tally kept by a stall that swaps goods, whose columns were declared without their arguments, so each takes the
-- declaration's defaults: decimal(18,0), numeric(18,0), char(1), varchar(1), time(7), datetimeoffset(7), datetime2(7), and a
-- float of 24 bits, which is real.
CREATE TABLE bazaar.swap_tally (
    tally_id int NOT NULL CONSTRAINT pk_swap_tally PRIMARY KEY,
    worth decimal NOT NULL,
    share numeric NOT NULL,
    grade char NOT NULL,
    mark varchar NOT NULL,
    opened time NOT NULL,
    logged datetimeoffset NOT NULL,
    counted datetime2 NOT NULL,
    weight float(24) NOT NULL
);
