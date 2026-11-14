-- A sack of flour, whose grade is a varchar set under the project's case-sensitive collation.
CREATE TABLE [mill].[Sack] (
    [SackId] INT NOT NULL CONSTRAINT [PK_Sack] PRIMARY KEY,
    [Grade] VARCHAR (8) NOT NULL CONSTRAINT [CK_Sack_Grade] CHECK ([Grade] IN ('fine', 'coarse')),
    [Kilos] DECIMAL (5, 2) NULL,
    [GrainId] INT NULL
);
