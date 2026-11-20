-- The channels an order is sold through, as a JSON array assembled from quoted literals with concat_ws, which leaves out
-- each NULL: any subset of the two, none included.
CREATE FUNCTION bead.channels (@channel varchar(10))
RETURNS nvarchar(100)
AS
BEGIN
    RETURN N'[' + concat_ws(N',',
        CASE WHEN @channel IN ('retail', 'both') THEN N'"retail"' END,
        CASE WHEN @channel IN ('trade', 'both') THEN N'"trade"' END) + N']';
END
GO
