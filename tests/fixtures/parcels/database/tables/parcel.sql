-- A courier's parcels. Three columns hold JSON an application writes, each declared: options, of a schema whose keys may be
-- left out or be null; route, an array of stops of two kinds, from a definition inside a shared file, named below; and extra,
-- any JSON at all.
CREATE TABLE parcels.parcel
(
    parcel_id int NOT NULL PRIMARY KEY,
    label nvarchar(40) NOT NULL,
    options nvarchar(max) NULL,
    route varchar(4000) NOT NULL,
    extra nvarchar(max) NULL
);
GO
-- route's schema is named here, as haruspex.json names the others' (decision 0141), so that a package reads it too.
EXECUTE sp_addextendedproperty @name = N'haruspex.jsonSchema', @value = N'schemas/stops.schema.json#/$defs/route', @level0type = N'SCHEMA', @level0name = N'parcels', @level1type = N'TABLE', @level1name = N'parcel', @level2type = N'COLUMN', @level2name = N'route';
GO
