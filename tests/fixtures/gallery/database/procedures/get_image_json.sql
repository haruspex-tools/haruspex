CREATE PROCEDURE gallery.get_image_json @image_id int AS
SELECT p.image_id, p.thumbnail, p.checksum, p.row_version, p.original
FROM gallery.product_image AS p
WHERE p.image_id = @image_id
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
