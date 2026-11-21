-- Through a variable, and through a scalar function that returns the column.
CREATE PROCEDURE parcels.options_of_one @parcel_id int
AS
DECLARE @options nvarchar(max) = (SELECT p.options FROM parcels.parcel AS p WHERE p.parcel_id = @parcel_id);
SELECT json_query(@options) AS options, json_query(parcels.options_of(@parcel_id)) AS called
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
