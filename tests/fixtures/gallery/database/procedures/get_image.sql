CREATE PROCEDURE gallery.get_image @image_id int AS
SELECT p.image_id, p.thumbnail, p.checksum, p.row_version, p.original
FROM gallery.product_image AS p
WHERE p.image_id = @image_id;
