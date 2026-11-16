-- Guards: a parameter tested NULL before a THROW is never NULL after it; @@ROWCOUNT tested straight after a SELECT that may
-- assign nothing proves it assigned every variable, each holding what its column held; and a count from an aggregate
-- without GROUP BY is assigned however many sales match, none included.
CREATE PROCEDURE flow.sale_card
    @sale_id int
AS
SET NOCOUNT ON;

IF @sale_id IS NULL
    THROW 50002, N'@sale_id is required', 1;

DECLARE @amount decimal(9, 2), @channel varchar(6), @note nvarchar(40), @others bigint;
SELECT @amount = s.amount, @channel = s.channel, @note = s.note FROM flow.sale AS s WHERE s.sale_id = @sale_id;
IF @@ROWCOUNT = 0
    RETURN;

SELECT @others = COUNT_BIG(*) FROM flow.sale AS o WHERE o.channel = @channel AND o.sale_id <> @sale_id;

SELECT @sale_id AS sale_id, @amount AS amount, @channel AS channel, @note AS note, @others AS others_on_channel;
