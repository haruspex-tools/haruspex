-- The post-deployment script, which includes another with :r. A deployment runs it every time, so what it turns off stays off,
-- and what it alters is what the database holds.
PRINT 'seeding';
GO
ALTER TABLE grove.tree NOCHECK CONSTRAINT ck_tree_variety;
GO
:r .\seed.sql
GO
ALTER PROCEDURE grove.count_trees AS
SELECT CAST(NULL AS varchar(10)) AS trees;
GO
