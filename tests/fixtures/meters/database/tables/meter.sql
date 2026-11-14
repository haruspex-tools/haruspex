CREATE SCHEMA meter;
GO
CREATE TABLE meter.meter (
    meter_id int NOT NULL CONSTRAINT pk_meter PRIMARY KEY,
    label varchar(20) NOT NULL,
    site varchar(20) NULL
);
