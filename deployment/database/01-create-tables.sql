-- =====================================================================
-- AMS — Amanat Management System · table definitions (SQL Server)
-- Mirrors Data/Migrations (source of truth: the EF Core migrations).
-- Run on an EMPTY database, then 02-create-indexes.sql.
-- =====================================================================

-- Customers — the people the shop lends to (plan §2, §5).
CREATE TABLE Customers (
    Id        INT IDENTITY(1,1) NOT NULL,
    Name      NVARCHAR(150)     NOT NULL,
    Phone     NVARCHAR(20)      NOT NULL,
    Address   NVARCHAR(400)     NOT NULL,
    Notes     NVARCHAR(500)     NOT NULL,
    CreatedAt DATETIME2         NOT NULL,
    CONSTRAINT PK_Customers PRIMARY KEY (Id)
);

-- Users — admin-created accounts; role = Admin | Lender | ViewOnly.
CREATE TABLE Users (
    Id           INT IDENTITY(1,1) NOT NULL,
    Name         NVARCHAR(100)     NOT NULL,
    Email        NVARCHAR(150)     NOT NULL,
    PasswordHash NVARCHAR(500)     NOT NULL,
    Role         NVARCHAR(20)      NOT NULL,
    IsActive     BIT               NOT NULL,
    CreatedAt    DATETIME2         NOT NULL,
    CONSTRAINT PK_Users PRIMARY KEY (Id)
);

-- Entries — one lend against a collateral item. Several rows share a
-- GroupId: the first is the primary lend, later ones are top-up sub-lends.
-- Money is DECIMAL(12,2); the rate is % per month (NULL = interest-free).
-- On release: InterestCollected freezes, AmountReturned = outstanding
-- principal + interest, ReleasedDate is stamped, TotalCollected rides
-- on the group's primary row.
CREATE TABLE Entries (
    Id              INT IDENTITY(1,1) NOT NULL,
    GroupId         INT               NOT NULL,
    CustomerId      INT               NOT NULL,
    EntryDate       DATE              NOT NULL,
    -- The lending YEAR, stored (business requirement): reports/filters key
    -- off this column directly instead of re-deriving it from the date.
    -- The app stamps it from EntryDate on every insert/update.
    EntryYear       INT               NOT NULL,
    ItemDescription NVARCHAR(500)     NOT NULL,
    AmountLend      DECIMAL(12,2)     NOT NULL,
    InterestRate    DECIMAL(5,2)      NULL,
    ExpectedReturn  DATE              NULL,
    Status          NVARCHAR(10)      NOT NULL,   -- Active | Released
    AmountReturned  DECIMAL(12,2)     NULL,
    InterestCollected DECIMAL(12,2)   NULL,
    TotalCollected  DECIMAL(12,2)     NULL,
    ReleasedDate    DATE              NULL,
    Notes           NVARCHAR(500)     NOT NULL,
    CreatedByUserId INT               NULL,
    CreatedAt       DATETIME2         NOT NULL,
    ModifiedAt      DATETIME2         NULL,
    CONSTRAINT PK_Entries PRIMARY KEY (Id),
    -- The one physical FK: a lend always belongs to a real customer.
    CONSTRAINT FK_Entries_Customers_CustomerId FOREIGN KEY (CustomerId)
        REFERENCES Customers (Id) ON DELETE CASCADE
);

-- PartialReturns — early partial money back against a still-active group.
-- Oldest lend is reduced first (FIFO), app-enforced.
CREATE TABLE PartialReturns (
    Id               INT IDENTITY(1,1) NOT NULL,
    GroupId          INT               NOT NULL,
    PaymentDate      DATE              NOT NULL,
    Amount           DECIMAL(12,2)     NOT NULL,
    Notes            NVARCHAR(500)     NULL,
    RecordedByUserId INT               NULL,
    CreatedAt        DATETIME2         NOT NULL,
    CONSTRAINT PK_PartialReturns PRIMARY KEY (Id)
);

-- CashLedger — THE Total Cash Available table (§3.5). Every rupee moving
-- over the counter is a stored row; SUM(Amount) is the balance. A LEND
-- posts one negative row (money out); a release posts TWO rows
-- (principal + interest, dated the release day); a partial return posts
-- one (ReferenceId = its PartialReturns.Id, kept in step on edits); a
-- manual +/− drawer movement posts one signed row. Only Manual rows are
-- deletable — lending rows belong to their lend/release records.
CREATE TABLE CashLedger (
    Id               INT IDENTITY(1,1) NOT NULL,
    LedgerDate       DATE              NOT NULL,
    Amount           DECIMAL(12,2)     NOT NULL,   -- signed for Manual (+ in / − out);
                                                   -- negative for Lend, positive
                                                   -- for the payback rows
    Source           NVARCHAR(30)      NOT NULL,   -- Lend | PartialReturn | ReleasePrincipal | ReleaseInterest | Manual
    GroupId          INT               NULL,       -- lending rows link back
    ReferenceId      INT               NULL,       -- Entries.Id on Lend rows; PartialReturns.Id on PartialReturn rows
    Notes            NVARCHAR(500)     NULL,
    RecordedByUserId INT               NULL,
    CreatedAt        DATETIME2         NOT NULL,
    CONSTRAINT PK_CashLedger PRIMARY KEY (Id)
);

-- Seed the first admin (password: admin123 — change after first login).
-- The hash below is the same static hash the migrations pin.
INSERT INTO Users (Name, Email, PasswordHash, Role, IsActive, CreatedAt)
VALUES ('Admin', 'admin@lenders.in',
        '$2a$11$hvHfLB5KAa.loAm1KdZ3feWxgLOEjC0P5T/K5QuAbhP3rR0U8nrSO',
        'Admin', 1, SYSUTCDATETIME());