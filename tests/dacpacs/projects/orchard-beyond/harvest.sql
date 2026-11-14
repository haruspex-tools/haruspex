-- Outside orchard's folder, built through an explicit Build item.
CREATE PROCEDURE grove.harvest AS
SELECT t.variety, COUNT(*) AS trees FROM grove.tree AS t GROUP BY t.variety;
