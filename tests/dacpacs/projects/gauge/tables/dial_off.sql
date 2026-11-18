CREATE TABLE dbo.dial_off (
    dial_id int NOT NULL CONSTRAINT pk_dial_off PRIMARY KEY,
    reading int NULL,
    unread AS (CASE WHEN reading = NULL THEN reading ELSE 0 END)
);
