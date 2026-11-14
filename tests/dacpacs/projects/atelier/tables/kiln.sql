-- A kiln: every kind of column and constraint a table can declare inline.
CREATE TABLE studio.kiln (
    kiln_id int IDENTITY(1, 1) NOT NULL CONSTRAINT pk_kiln PRIMARY KEY,
    label studio.code,
    glaze char(3) COLLATE Latin1_General_BIN2 NULL CHECK (glaze IN ('ash', 'tin')),
    fired_at datetime2(0) NULL,
    peak int NULL CONSTRAINT ck_kiln_peak CHECK NOT FOR REPLICATION (peak < 1400),
    peak_f AS peak * 9 / 5 + 32,
    peak_k AS ISNULL(peak, 0) + 273 PERSISTED NOT NULL,
    serial varchar(12) NOT NULL CONSTRAINT uq_kiln_serial UNIQUE,
    CONSTRAINT ck_kiln_id CHECK (kiln_id > 0),
    CHECK (fired_at > '2000-01-01'),
    INDEX ix_kiln_label UNIQUE (label),
    INDEX ix_kiln_fired UNIQUE (fired_at) WHERE fired_at IS NOT NULL
);
GO
CREATE UNIQUE INDEX ix_kiln_serial_peak ON studio.kiln (serial, peak);
GO
ALTER TABLE studio.kiln WITH NOCHECK ADD CONSTRAINT ck_kiln_late CHECK (peak > 0);
GO
ALTER TABLE studio.kiln NOCHECK CONSTRAINT ck_kiln_id;
