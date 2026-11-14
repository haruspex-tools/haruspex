CREATE TABLE meter.reading (
    reading_id int NOT NULL CONSTRAINT pk_reading PRIMARY KEY,
    meter_id int NOT NULL CONSTRAINT fk_reading_meter REFERENCES meter.meter (meter_id),
    read_on date NOT NULL,
    kwh int NOT NULL,
    peak_kw decimal(9,3) NOT NULL
);
