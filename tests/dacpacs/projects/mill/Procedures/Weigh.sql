-- Built with QUOTED_IDENTIFIER off by its item's metadata: "kg" is a string.
CREATE PROCEDURE [mill].[Weigh]
AS
SELECT s.[SackId], s.[Kilos], "kg" AS [Unit] FROM [mill].[Sack] AS s;
