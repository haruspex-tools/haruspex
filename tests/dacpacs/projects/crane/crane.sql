-- In the same database as quay's berths, from a project of its own.
CREATE TABLE dock.crane (
    crane_id int NOT NULL CONSTRAINT pk_crane PRIMARY KEY,
    berth_id int NOT NULL CONSTRAINT fk_crane_berth REFERENCES dock.berth (berth_id),
    lift_t smallint NOT NULL CONSTRAINT ck_crane_lift CHECK (lift_t BETWEEN 1 AND 500)
);
