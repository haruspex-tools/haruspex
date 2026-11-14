-- Built with ANSI_NULLS off by its item's metadata.
CREATE PROCEDURE grove.nulls_off @row smallint AS
SELECT t.tree_id FROM grove.tree AS t WHERE t.row_no = @row;
