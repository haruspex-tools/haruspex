CREATE TABLE dbo.dial (
    dial_id int NOT NULL CONSTRAINT pk_dial PRIMARY KEY,
    reading int NULL,
    label varchar(10) NOT NULL,
    is_high AS (CASE WHEN reading > 5 THEN 1 ELSE 0 END),
    doubled AS (CAST(reading * 2 AS bigint)) PERSISTED,
    tag AS (CONCAT(label, '-x')) PERSISTED NOT NULL,
    unread AS (CASE WHEN reading = NULL THEN reading ELSE 0 END)
);
