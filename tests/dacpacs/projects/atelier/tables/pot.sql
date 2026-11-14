CREATE TABLE studio.pot (
    pot_id int NOT NULL CONSTRAINT pk_pot PRIMARY KEY,
    kiln_id int NOT NULL CONSTRAINT fk_pot_kiln REFERENCES studio.kiln (kiln_id) ON DELETE CASCADE,
    note nvarchar(40) NULL,
    valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    valid_to datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (valid_from, valid_to)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = studio.pot_history));
