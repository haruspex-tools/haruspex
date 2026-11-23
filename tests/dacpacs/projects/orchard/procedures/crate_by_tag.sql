-- A crate by its tag: at most one only while the tag's key holds.
CREATE PROCEDURE grove.crate_by_tag @tag varchar(8) AS
SELECT c.crate_id, c.size_class FROM grove.crate AS c WHERE c.tag = @tag;
