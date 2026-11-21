CREATE FUNCTION parcels.options_of(@parcel_id int)
RETURNS nvarchar(max)
AS
BEGIN
    RETURN (SELECT p.options FROM parcels.parcel AS p WHERE p.parcel_id = @parcel_id);
END;
