-- One sample measured in an assay: a concentration to four places, a dilution to two, and a column of each other kind a
-- rounding reads: money, a float, a string holding a number, and decimals at the edges of 38 digits.
CREATE TABLE assay.sample (
    sample_id int NOT NULL CONSTRAINT pk_sample PRIMARY KEY,
    concentration decimal(10,4) NOT NULL,
    dilution decimal(10,2) NOT NULL,
    reference decimal(38,0) NOT NULL,
    fee money NOT NULL,
    absorbance float NOT NULL,
    lab_value varchar(20) NOT NULL,
    ratio decimal(20,10) NOT NULL,
    long_ratio decimal(38,10) NOT NULL,
    fine_scale decimal(37,14) NOT NULL,
    big_count decimal(35,7) NOT NULL
);
