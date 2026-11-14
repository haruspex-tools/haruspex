-- The post-deployment script, which includes another with :r.
PRINT 'seeding';
GO
:r .\seed.sql
