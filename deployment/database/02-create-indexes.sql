-- =====================================================================
-- AMS · indexes (mirror of Data/Migrations — run after 01-create-tables.sql)
-- =====================================================================

CREATE INDEX IX_Customers_Name ON Customers (Name);            -- the suggestion picker
CREATE INDEX IX_Entries_CustomerId ON Entries (CustomerId);    -- customer history/outstanding
CREATE INDEX IX_Entries_GroupId ON Entries (GroupId);          -- dashboard's grouped view
CREATE INDEX IX_Entries_EntryYear ON Entries (EntryYear);      -- the stored lending year (reports/filters)
CREATE INDEX IX_Entries_Status_EntryDate ON Entries (Status, EntryDate);  -- tab slices + sort
CREATE INDEX IX_PartialReturns_GroupId ON PartialReturns (GroupId);
CREATE UNIQUE INDEX IX_Users_Email ON Users (Email);           -- emails are logins
CREATE INDEX IX_CashLedger_LedgerDate ON CashLedger (LedgerDate);
CREATE INDEX IX_CashLedger_Source ON CashLedger (Source);
CREATE INDEX IX_CashLedger_ReferenceId ON CashLedger (ReferenceId);  -- partial-return sync on edit