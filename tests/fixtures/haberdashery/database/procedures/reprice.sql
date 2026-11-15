-- OUTPUT stars over a temporal table and a table with a column set: what a star over each leaves out, they leave out too.
CREATE PROCEDURE shop.reprice @sku varchar(12)
AS
SET NOCOUNT ON;

UPDATE shop.price SET amount = amount + 0.10 OUTPUT inserted.*, deleted.amount AS old_amount WHERE sku = @sku;

UPDATE shop.parcel SET label = N'Checked' OUTPUT deleted.* WHERE parcel_id < 3;
