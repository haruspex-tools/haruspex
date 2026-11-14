-- A grain the mill grinds.
CREATE TABLE [mill].[Grain] (
    [GrainId] INT NOT NULL CONSTRAINT [PK_Grain] PRIMARY KEY,
    [Variety] NVARCHAR (40) NOT NULL
);
