-- Application.People, from WideWorldImporters' Application/Tables/People.sql (Microsoft, MIT licence: license.txt in this
-- fixture's folder): the table as Microsoft defines it, less the default drawn from a sequence, which this fixture doesn't
-- define. Its indexes and descriptions are left out; they don't change what a query returns.
CREATE TABLE [Application].[People] (
    [PersonID]                INT                                         NOT NULL,
    [FullName]                NVARCHAR (50)                               NOT NULL,
    [PreferredName]           NVARCHAR (50)                               NOT NULL,
    [SearchName]              AS                                          (concat([PreferredName],N' ',[FullName])) PERSISTED NOT NULL,
    [IsPermittedToLogon]      BIT                                         NOT NULL,
    [LogonName]               NVARCHAR (256)                              NULL,
    [IsExternalLogonProvider] BIT                                         NOT NULL,
    [HashedPassword]          VARBINARY (MAX)                             NULL,
    [IsSystemUser]            BIT                                         NOT NULL,
    [IsEmployee]              BIT                                         NOT NULL,
    [IsSalesperson]           BIT                                         NOT NULL,
    [UserPreferences]         NVARCHAR (MAX)                              NULL,
    [PhoneNumber]             NVARCHAR (20)                               NULL,
    [FaxNumber]               NVARCHAR (20)                               NULL,
    [EmailAddress]            NVARCHAR (256)                              NULL,
    [Photo]                   VARBINARY (MAX)                             NULL,
    [CustomFields]            NVARCHAR (MAX)                              NULL,
    [OtherLanguages]          AS                                          (json_query([CustomFields],N'$.OtherLanguages')),
    [LastEditedBy]            INT                                         NOT NULL,
    [ValidFrom]               DATETIME2 (7) GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]                 DATETIME2 (7) GENERATED ALWAYS AS ROW END   NOT NULL,
    CONSTRAINT [PK_Application_People] PRIMARY KEY CLUSTERED ([PersonID] ASC),
    CONSTRAINT [FK_Application_People_Application_People] FOREIGN KEY ([LastEditedBy]) REFERENCES [Application].[People] ([PersonID]),
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE=[Application].[People_Archive], DATA_CONSISTENCY_CHECK=ON));
