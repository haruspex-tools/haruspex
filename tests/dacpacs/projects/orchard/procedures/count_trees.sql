-- The post-deployment script alters this one, so what a deployment leaves is its ALTER, not this.
CREATE PROCEDURE grove.count_trees AS
SELECT COUNT(*) AS trees FROM grove.tree;
