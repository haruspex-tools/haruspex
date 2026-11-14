-- Reads a table of this project and one of press's.
CREATE PROCEDURE grove.list_trees AS
SELECT t.tree_id, t.variety, p.pressed_on
FROM grove.tree AS t
LEFT JOIN grove.pressing AS p ON p.pressing_id = t.pressed_id;
