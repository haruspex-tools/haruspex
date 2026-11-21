-- A courier's parcels. Three columns hold JSON an application writes, each declared in haruspex.json: options, of a schema
-- whose keys may be left out or be null; route, an array of stops of two kinds, from a definition inside a shared file; and
-- extra, any JSON at all.
CREATE TABLE parcels.parcel
(
    parcel_id int NOT NULL PRIMARY KEY,
    label nvarchar(40) NOT NULL,
    options nvarchar(max) NULL,
    route varchar(4000) NOT NULL,
    extra nvarchar(max) NULL
);
