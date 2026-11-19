-- Three images: a thumbnail as long as its column allows with an original past 8,000 bytes (which FOR JSON writes as base64 past
-- 8,000 bytes too), one with neither, and one of no bytes at all. The checksum is the thumbnail's SHA-256, as add_image writes it.
-- REPLICATE gives varchar of a binary value's bytes, so each is converted back.
INSERT INTO gallery.product_image (image_id, thumbnail, checksum, original)
VALUES
    (1, CAST(REPLICATE(CAST(0xAB AS varbinary(max)), 8000) AS varbinary(8000)), HASHBYTES('SHA2_256', CAST(REPLICATE(CAST(0xAB AS varbinary(max)), 8000) AS varbinary(8000))), CAST(REPLICATE(CAST(0xCD AS varbinary(max)), 9000) AS varbinary(max))),
    (2, NULL, HASHBYTES('SHA2_256', 0x), NULL),
    (3, 0x, HASHBYTES('SHA2_256', 0x), 0x);

INSERT INTO gallery.legacy_scan (scan_id, scan)
VALUES (1, 0x0102030405), (2, NULL), (3, 0x);
