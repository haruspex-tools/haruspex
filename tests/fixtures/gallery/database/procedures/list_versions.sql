CREATE PROCEDURE gallery.list_versions AS
SELECT p.checksum AS bytes FROM gallery.product_image AS p
UNION ALL
SELECT p.thumbnail FROM gallery.product_image AS p;
