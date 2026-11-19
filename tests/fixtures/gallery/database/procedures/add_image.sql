CREATE PROCEDURE gallery.add_image @image_id int, @thumbnail varbinary(8000), @original varbinary(max) = NULL AS
INSERT INTO gallery.product_image (image_id, thumbnail, checksum, original)
VALUES (@image_id, @thumbnail, HASHBYTES('SHA2_256', ISNULL(@thumbnail, 0x)), @original);

SELECT p.image_id, p.checksum, p.row_version, DATALENGTH(p.thumbnail) AS thumbnail_bytes
FROM gallery.product_image AS p
WHERE p.image_id = @image_id;
