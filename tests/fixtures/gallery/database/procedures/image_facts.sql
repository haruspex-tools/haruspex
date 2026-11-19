CREATE PROCEDURE gallery.image_facts AS
SELECT
    p.image_id,
    CAST(p.image_id AS varbinary) AS id_bytes,
    CONVERT(varbinary(4), '0x0A0B', 1) AS from_hex,
    CONVERT(varbinary(max), 'CAFE', 2) AS from_bare_hex,
    CONVERT(varchar(66), p.checksum, 1) AS checksum_hex,
    CONVERT(varchar(18), CONVERT(binary(8), p.row_version), 1) AS version_hex,
    CAST(p.checksum AS varbinary(4)) AS checksum_head,
    COALESCE(p.thumbnail, p.checksum) AS thumbnail_or_checksum,
    ISNULL(p.thumbnail, 0x) AS thumbnail_or_empty,
    CASE WHEN p.image_id = 1 THEN p.checksum ELSE 0x01 END AS chosen,
    p.checksum + p.checksum AS doubled,
    0x01 + 0x0203 AS joined,
    CONCAT(p.checksum, 0x21) AS as_text,
    SUBSTRING(p.checksum, 1, 4) AS checksum_part,
    LEFT(p.checksum, 2) AS checksum_left,
    HASHBYTES('SHA2_512', p.checksum) AS sha512,
    HASHBYTES('MD5', CAST(p.image_id AS varchar(10))) AS md5,
    HASHBYTES(CASE WHEN p.image_id = 1 THEN 'MD5' ELSE 'XYZ' END, p.checksum) AS chosen_hash,
    COMPRESS(p.checksum) AS packed,
    DECOMPRESS(COMPRESS(p.checksum)) AS unpacked,
    DATALENGTH(p.original) AS original_bytes
FROM gallery.product_image AS p;
