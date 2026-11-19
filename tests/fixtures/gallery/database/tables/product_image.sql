-- A shop's product images: a thumbnail of at most 8,000 bytes, the SHA-256 checksum of it, the row's version, and the original
-- picture, of any length.
CREATE TABLE gallery.product_image (
    image_id int NOT NULL CONSTRAINT pk_product_image PRIMARY KEY,
    thumbnail varbinary(8000) NULL,
    checksum binary(32) NOT NULL,
    row_version rowversion,
    original varbinary(max) NULL);
