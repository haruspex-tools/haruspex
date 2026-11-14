-- Reads a table of this project and one of granary's.
CREATE PROCEDURE [mill].[ListSacks]
AS
SELECT s.[SackId], s.[Grade], g.[Variety]
FROM [mill].[Sack] AS s
LEFT JOIN [mill].[Grain] AS g ON g.[GrainId] = s.[GrainId];
