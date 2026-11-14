CREATE SCHEMA dock;
GO
-- The bottom of the chain: harbour reaches it only through crane.
CREATE TABLE dock.berth (
    berth_id int NOT NULL CONSTRAINT pk_berth PRIMARY KEY,
    name nvarchar(30) NOT NULL,
    depth_m decimal(4, 1) NULL
);
