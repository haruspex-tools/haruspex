-- A thin query file: its name names the function (getBakeSheet), its variable the parameter (@ovenId, passed on to
-- batch_rows' @oven), and WITH RESULT SETS the fields of each set, typed as declared.
EXEC calls.batch_rows @oven = @ovenId
WITH RESULT SETS (
    (batchId int NOT NULL, product nvarchar(30) NOT NULL, grams int NULL),
    (ovenId int NOT NULL, label nvarchar(20) NOT NULL, racks tinyint NULL)
);
