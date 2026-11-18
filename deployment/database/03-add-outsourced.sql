-- §4.8 Outsourcing — deploy script for a database created from
-- 01-create-tables.sql / 02-create-indexes.sql (no EF history yet):
-- creates the __EFMigrationsHistory table, backfills the history rows of
-- the schema that already exists, then adds the outsourcing schema.
-- EVERY statement is guarded — safe to re-run.
-- If a past run left an open transaction, run `ROLLBACK` first.

IF OBJECT_ID(N'__EFMigrationsHistory') IS NULL
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId]    nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32)  NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId]));
GO

-- The schema already on file corresponds to these five migrations.
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261030135409_InitialCreate')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261030135409_InitialCreate', N'10.0.14');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261030184707_AddCashAdjustments')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261030184707_AddCashAdjustments', N'10.0.14');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261030202509_CashLedgerTable')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261030202509_CashLedgerTable', N'10.0.14');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261030212400_AddEntryYear')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261030212400_AddEntryYear', N'10.0.14');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261102181103_AddActivityLog')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20261102181103_AddActivityLog', N'10.0.14');
GO

IF COL_LENGTH(N'[Entries]', N'IsOutsourced') IS NULL
    ALTER TABLE [Entries] ADD [IsOutsourced] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

IF OBJECT_ID(N'[OutsourcedItems]') IS NULL
BEGIN
    CREATE TABLE [OutsourcedItems] (
        [Id] int NOT NULL IDENTITY,
        [GroupId] int NOT NULL,
        [OutsourcedTo] nvarchar(150) NOT NULL,
        [OutsourceDate] date NOT NULL,
        [Notes] nvarchar(500) NULL,
        [ReturnedDate] date NULL,
        [CreatedByUserId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_OutsourcedItems] PRIMARY KEY ([Id])
    );

    CREATE INDEX [IX_OutsourcedItems_GroupId] ON [OutsourcedItems] ([GroupId]);
    CREATE INDEX [IX_OutsourcedItems_OutsourceDate] ON [OutsourcedItems] ([OutsourceDate]);
END
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261118100728_AddOutsourced')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261118100728_AddOutsourced', N'10.0.14');
GO
