CREATE PROCEDURE gallery.sign_image @image_id int, @expected rowversion = NULL, @signature binary(32) OUTPUT AS
SELECT @signature = p.checksum
FROM gallery.product_image AS p
WHERE p.image_id = @image_id AND (@expected IS NULL OR p.row_version = @expected);
