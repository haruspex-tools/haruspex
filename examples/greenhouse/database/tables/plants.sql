CREATE TABLE garden.plant
(
    plant_id  int          NOT NULL CONSTRAINT pk_plant PRIMARY KEY,
    name      nvarchar(60) NOT NULL,
    kind      nvarchar(10) NOT NULL CONSTRAINT ck_plant_kind CHECK (kind IN (N'herb', N'shrub', N'tree')),
    planted   date         NULL
);
GO
CREATE TABLE garden.water_log
(
    entry_id     int          NOT NULL CONSTRAINT pk_water_log PRIMARY KEY,
    plant_id     int          NOT NULL CONSTRAINT fk_water_log_plant REFERENCES garden.plant (plant_id),
    watered_at   datetime2(0) NOT NULL,
    litres       decimal(5,2) NOT NULL CONSTRAINT ck_water_log_litres CHECK (litres > 0)
);
