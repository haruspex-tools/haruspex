-- Built with ANSI_NULLS off by its item's metadata.
CREATE PROCEDURE [mill].[Loose] @Kilos DECIMAL (5, 2)
AS
SELECT s.[SackId] FROM [mill].[Sack] AS s WHERE s.[Kilos] = @Kilos;
