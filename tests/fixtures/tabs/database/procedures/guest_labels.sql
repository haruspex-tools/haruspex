-- Arguments that are never null stay never null inside the function: the name is NOT NULL, the nickname is not.
CREATE PROCEDURE cafe.guest_labels
AS
SET NOCOUNT ON;

SELECT g.guest_id, l.display_name, l.full_label, l.greeting, l.nickname
FROM cafe.guest AS g
CROSS APPLY cafe.guest_label(g.name, g.nickname) AS l
ORDER BY g.guest_id;
