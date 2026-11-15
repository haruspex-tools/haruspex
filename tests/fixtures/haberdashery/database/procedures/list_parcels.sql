-- A star over a table with a column set.
CREATE PROCEDURE shop.list_parcels
AS
SET NOCOUNT ON;

SELECT * FROM shop.parcel ORDER BY parcel_id;
