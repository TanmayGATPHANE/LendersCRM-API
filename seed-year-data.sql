-- ============================================================================
--  AMS — one year of business, straight into SQL Server
--  Target : HP_WORKSTATION\SQLEXPRESS, database AmsDb
--  Run    : SSMS (F5) or:  sqlcmd -S "HP_WORKSTATION\SQLEXPRESS" -d AmsDb -i seed-year-data.sql
--
--  What it does
--  ------------
--  Wipes the BUSINESS tables (Customers, Entries, PartialReturns, CashLedger
--  — Users are left alone, admin login survives), then replays a full year
--  of counter activity across Nov 2025 → Oct 2026:
--
--    · 1,000 customers (40 first names × 25 surnames) — 10 busy regulars
--      plus a very long tail
--    · 720 collateral groups (60 per month, day-jittered); every 2nd group
--      takes a top-up sub-lend, every 4th takes a second → ~1,100 lends
--    · odd-rupee amounts (real counters never lend round numbers)
--    · ~⅓ released (~240 books), with the app's EXACT semantics (GroupService):
--        - partials clear the OLDEST lends first (FIFO, Interest.AllocatePaid)
--        - interest frozen per lend: ROUND(principal-out × rate/100 × days/30)
--        - AmountReturned = principal-out + frozen interest
--        - TotalCollected rides on the group's primary (earliest) entry
--        - TWO CashLedger legs on release day: ReleasePrincipal + ReleaseInterest
--    · partial returns mid-life (PartialReturns row + PartialReturn cash row
--      carrying its ReferenceId, dated the payment day)
--    · one 'Lend' cash row per lend, dated the lend day (money going out)
--    · manual ± cash movements (starting capital, shop expenses, partner top-ups)
--
--  Every insert mirrors what EntryService / GroupService / PaymentService
--  write through EF Core — including EntryYear, which the DbContext stamps
--  on every save; the script sets it the same way. At the end it prints a
--  month-by-month ledger check and the outstanding ranking so you can
--  eyeball what the Reports charts are about to plot.
--
--  ⚠ DESTRUCTIVE: running it replaces ALL existing business data.
-- ============================================================================

USE AmsDb;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
BEGIN TRAN;

-- ── 0. Wipe the books (users survive) ──────────────────────────────────────
DELETE FROM dbo.CashLedger;
DELETE FROM dbo.PartialReturns;
DELETE FROM dbo.Entries;
DELETE FROM dbo.Customers;
DBCC CHECKIDENT ('dbo.CashLedger',     RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.PartialReturns', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Entries',        RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Customers',      RESEED, 0) WITH NO_INFOMSGS;

-- ── 1. Customers — 1,000 of them: 40 first names × 25 surnames ────────────
DECLARE @first TABLE (Idx int IDENTITY(0,1), Name nvarchar(60));
INSERT INTO @first (Name) VALUES
  (N'Rakesh'), (N'Sunita'), (N'Imran'), (N'Vandana'), (N'Prakash'), (N'Rekha'),
  (N'Maruti'), (N'Farida'), (N'Deepak'), (N'Lata'), (N'Ganesh'), (N'Heena'),
  (N'Suresh'), (N'Mrunal'), (N'Azad'), (N'Varsha'), (N'Omprakash'), (N'Sneha'),
  (N'Rajesh'), (N'Pooja'), (N'Salim'), (N'Kavita'), (N'Dinesh'), (N'Meena'),
  (N'Vinod'), (N'Shraddha'), (N'Taufiq'), (N'Jyoti'), (N'Anil'), (N'Prerna'),
  (N'Mahesh'), (N'Swapnali'), (N'Nitin'), (N'Ayesha'), (N'Kiran'), (N'Neha'),
  (N'Bhaskar'), (N'Rukhsar'), (N'Atul'), (N'Madhuri');

DECLARE @last TABLE (Idx int IDENTITY(0,1), Name nvarchar(60));
INSERT INTO @last (Name) VALUES
  (N'Sharma'), (N'Mhatre'), (N'Qureshi'), (N'Joshi'), (N'Naidu'), (N'Panchal'),
  (N'Patil'), (N'Shaikh'), (N'Sawant'), (N'Kamble'), (N'Bhosale'), (N'Ansari'),
  (N'Poojari'), (N'Deshpande'), (N'Khan'), (N'Gaikwad'), (N'Jat'), (N'Raut'),
  (N'More'), (N'Shastry'), (N'Sheikh'), (N'Kulkarni'), (N'Yadav'), (N'Pawar'),
  (N'Dalvi');

DECLARE @areas TABLE (Idx int IDENTITY(0,1), Area nvarchar(60));
INSERT INTO @areas (Area) VALUES
  (N'Thane West'), (N'Dombivli East'), (N'Mumbra'), (N'Kalwa'), (N'Bhiwandi'),
  (N'Dombivli West'), (N'Thane East'), (N'Ghodbunder'), (N'Kalyan West'),
  (N'Ambernath'), (N'Vithalwadi'), (N'Ulhasnagar');

DECLARE @cust TABLE (Idx int IDENTITY(0,1), Id int, Name nvarchar(150), Area nvarchar(60));
INSERT INTO @cust (Name, Area)
SELECT f.Name + N' ' + l.Name, (SELECT Area FROM @areas a WHERE a.Idx = (f.Idx * 25 + l.Idx) % 12)
FROM @first f CROSS JOIN @last l;

INSERT INTO dbo.Customers (Name, Phone, Address, Notes, CreatedAt)
SELECT c.Name,
       N'9' + RIGHT(N'000000000' + CONVERT(nvarchar(10), 810000000 + c.Idx * 7919 + 3412), 10),
       c.Area, N'', DATEADD(day, -365 + (c.Idx * 131) % 340, '2026-11-01')
FROM @cust c;

UPDATE c SET c.Id = cu.Id FROM @cust c JOIN dbo.Customers cu ON cu.Name = c.Name;

-- ── 2. The collateral ladder — what a Thane counter actually holds ────────
DECLARE @items TABLE (Idx int IDENTITY(0,1), Item nvarchar(60), Base int);
INSERT INTO @items (Item, Base) VALUES
  (N'Gold chain (12g)',      45200),
  (N'Gold bangles (22g)',    69500),
  (N'Gold ring (5g)',        18400),
  (N'Samsung Galaxy A54',    14800),
  (N'iPhone 12 (64gb)',      27300),
  (N'Dell Latitude laptop',  30500),
  (N'Bajaj Pulsar - RC',     62400),
  (N'Honda Activa - RC',     47600),
  (N'Sony Bravia 43in TV',   21400),
  (N'Canon EOS camera',      25800),
  (N'Gold mangalsutra (8g)', 33700),
  (N'OnePlus Nord CE 3',     12900);

-- ── 3. The plan: one row per collateral group ─────────────────────────────
-- Deterministic "randomness" (modular arithmetic only) so re-runs produce
-- the same book — handy when comparing a chart tweak against yesterday's.
DECLARE @groups TABLE (
    Idx         int IDENTITY(0,1),
    CustomerId  int,
    LendDate    date,
    Item        nvarchar(60),
    BaseAmt     int,
    NSub        int,            -- top-up lends beyond the first (0/1/2)
    ReleaseDate date NULL,      -- NULL = still on the shelf (Active)
    P1Date      date NULL, P1Amt int,
    P2Date      date NULL, P2Amt int
);

DECLARE @today date = '2026-11-02';
DECLARE @yearStart date = '2025-11-02';   -- the year begins here
DECLARE @i int = 0, @custId int, @itemId int, @lend date, @rel date,
        @p1 date, @p2 date, @nsub int, @jitter int;

WHILE @i < 720
BEGIN
    -- The 10 busy regulars take every 3rd group; the rest scatter over
    -- the wide book (10..999), so a few hundred names carry the year.
    IF @i % 3 = 0
        SET @custId = (SELECT Id FROM @cust WHERE Idx = @i % 10);
    ELSE
        SET @custId = (SELECT Id FROM @cust WHERE Idx = 10 + (@i * 37) % 990);

    SET @itemId = (@i * 7) % 12;

    -- ~55 groups a month across the 13 months (Nov 2025 → Nov 2026),
    -- day-jittered 2..27. Anything the plan drops past TODAY (the current
    -- month is still young) lands on the counter's first two November
    -- days instead — so the app's default "current month" view opens on
    -- fresh business, not an empty drawer.
    SET @lend = DATEADD(day, 1 + (@i * 11) % 26, DATEADD(month, @i % 13, @yearStart));
    IF @lend > @today
        SET @lend = DATEADD(day, (@i * 7) % 2, '2026-11-01');

    -- ~30% end released: 60–210 days held, never past today; every 10th is a
    -- same-day return (0 days held — exercises the degenerate release path).
    SET @rel = CASE
                 WHEN (@i % 10) IN (0, 3, 6)
                      AND DATEADD(day, 60 + (@i * 23) % 150, @lend) < @today
                     THEN DATEADD(day, 60 + (@i * 23) % 150, @lend)
                 WHEN (@i % 10) = 8 THEN @lend
               END;

    SET @nsub = CASE WHEN @i % 2 = 1 THEN 1 ELSE 0 END
              + CASE WHEN @i % 4 = 3 THEN 1 ELSE 0 END;

    -- Partials: most active groups pay at least once, some pay twice.
    SET @p1 = CASE WHEN @i % 3 <> 1 THEN DATEADD(day, 24 + (@i * 13) % 90, @lend) END;
    SET @p2 = CASE WHEN @i % 7 = 2 THEN DATEADD(day, 70 + (@i * 17) % 110, @lend) END;
    -- A partial must sit inside the loan's life: after lend, before release/today.
    IF @p1 IS NOT NULL AND ((@rel IS NOT NULL AND @p1 >= @rel) OR @p1 >= @today) SET @p1 = NULL;
    IF @p2 IS NOT NULL AND ((@rel IS NOT NULL AND @p2 >= @rel) OR @p2 >= @today OR @p1 IS NULL) SET @p2 = NULL;

    SELECT @jitter = Base + (@i * 137) % 997 FROM @items WHERE Idx = @itemId;

    INSERT INTO @groups (CustomerId, LendDate, Item, BaseAmt, NSub, ReleaseDate, P1Date, P1Amt, P2Date, P2Amt)
    VALUES (@custId, @lend,
            (SELECT Item FROM @items WHERE Idx = @itemId),
            @jitter, @nsub, @rel, @p1,
            CASE WHEN @p1 IS NULL THEN 0 ELSE 3000 + (@i * 733) % ((@jitter / 4) + 3000) END,
            @p2,
            CASE WHEN @p2 IS NULL THEN 0 ELSE 2500 + (@i * 571) % ((@jitter / 5) + 2500) END);

    SET @i += 1;
END;

-- ── 4. Replay: lends, top-ups, partials, releases, cash legs ──────────────
DECLARE @gid int, @eid int, @amt decimal(12,2), @rate decimal(5,2),
        @p1Amt int, @p2Amt int,
        @partialTotal decimal(12,2), @remaining decimal(12,2),
        @cover decimal(12,2), @frozen decimal(12,2),
        @principalBack decimal(12,2), @interestBack decimal(12,2),
        @id2 int, @amt2 decimal(12,2), @rate2 decimal(5,2), @ed2 date,
        @s int, @subDate date, @subAmt decimal(12,2);

DECLARE g CURSOR LOCAL FAST_FORWARD FOR
    SELECT Idx, CustomerId, LendDate, Item, BaseAmt, NSub, ReleaseDate, P1Date, P1Amt, P2Date, P2Amt
    FROM @groups;
OPEN g;

DECLARE @gIdx int, @item nvarchar(60);
FETCH NEXT FROM g INTO @gIdx, @custId, @lend, @item, @amt, @nsub, @rel, @p1, @p1Amt, @p2, @p2Amt;
WHILE @@FETCH_STATUS = 0
BEGIN
    -- 4a. The first lend BECOMES the group: insert, then self-point GroupId.
    SET @rate = CASE WHEN (@gIdx + 3) % 11 = 0 THEN NULL ELSE 1.5 + ((@gIdx * 5) % 4) * 0.5 END;

    INSERT INTO dbo.Entries (GroupId, CustomerId, EntryDate, EntryYear, ItemDescription,
                             AmountLend, InterestRate, Status, Notes, CreatedByUserId, CreatedAt)
    VALUES (0, @custId, @lend, YEAR(@lend), @item, @amt, @rate, N'Active',
            CASE WHEN (@gIdx + 1) % 9 = 0 THEN N'Festival loan - counter regular' ELSE N'' END,
            1, @lend);

    SET @gid = SCOPE_IDENTITY();
    UPDATE dbo.Entries SET GroupId = @gid WHERE Id = @gid;

    -- Cash: the money GOING OUT on the lend day.
    INSERT INTO dbo.CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
    VALUES (@lend, -@amt, N'Lend', @gid, @gid, NULL, 1, @lend);

    -- 4b. Top-up sub-lends (one story, each lend its own row + cash leg).
    SET @s = 1;
    WHILE @s <= @nsub
    BEGIN
        SET @subDate = DATEADD(day, 25 + (@gIdx * 19 * @s) % 80, @lend);
        IF @subDate >= ISNULL(@rel, @today) SET @subDate = DATEADD(day, -12, ISNULL(@rel, @today));
        IF @subDate > @lend AND @subDate < ISNULL(@rel, @today)
        BEGIN
            SET @subAmt = ROUND(@amt * (0.30 + 0.13 * ((@gIdx + @s) % 4)), 0) + (@gIdx * 311 * @s) % 877;
            INSERT INTO dbo.Entries (GroupId, CustomerId, EntryDate, EntryYear, ItemDescription,
                                     AmountLend, InterestRate, Status, Notes, CreatedByUserId, CreatedAt)
            VALUES (@gid, @custId, @subDate, YEAR(@subDate), @item, @subAmt,
                    CASE WHEN (@gIdx + @s) % 6 = 0 THEN NULL ELSE @rate END, N'Active',
                    CASE WHEN @s = 1 THEN N'Top-up lend' ELSE N'Second top-up' END,
                    1, @subDate);
            SET @eid = SCOPE_IDENTITY();
            INSERT INTO dbo.CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
            VALUES (@subDate, -@subAmt, N'Lend', @gid, @eid, NULL, 1, @subDate);
        END
        SET @s += 1;
    END

    -- 4c. Partial returns — a PartialReturns row + its dated cash row each.
    IF @p1 IS NOT NULL
    BEGIN
        INSERT INTO dbo.PartialReturns (GroupId, PaymentDate, Amount, Notes, RecordedByUserId, CreatedAt)
        VALUES (@gid, @p1, @p1Amt, N'Partial - cash at counter', 1, @p1);
        SET @eid = SCOPE_IDENTITY();
        INSERT INTO dbo.CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
        VALUES (@p1, @p1Amt, N'PartialReturn', @gid, @eid, NULL, 1, @p1);
    END
    IF @p2 IS NOT NULL
    BEGIN
        INSERT INTO dbo.PartialReturns (GroupId, PaymentDate, Amount, Notes, RecordedByUserId, CreatedAt)
        VALUES (@gid, @p2, @p2Amt, N'Second partial - salary came in', 1, @p2);
        SET @eid = SCOPE_IDENTITY();
        INSERT INTO dbo.CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
        VALUES (@p2, @p2Amt, N'PartialReturn', @gid, @eid, NULL, 1, @p2);
    END

    -- 4d. Release — the app's exact math, in one FIFO pass.
    IF @rel IS NOT NULL
    BEGIN
        SELECT @partialTotal = ISNULL(SUM(Amount), 0) FROM dbo.PartialReturns WHERE GroupId = @gid;
        SET @remaining = @partialTotal;
        SET @principalBack = 0;
        SET @interestBack = 0;

        DECLARE done CURSOR LOCAL FAST_FORWARD FOR
            SELECT Id, AmountLend, InterestRate, EntryDate FROM dbo.Entries
            WHERE GroupId = @gid AND Status = N'Active'
            ORDER BY EntryDate, Id;
        OPEN done;
        FETCH NEXT FROM done INTO @id2, @amt2, @rate2, @ed2;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- FIFO cover: partials clear the oldest lends first, capped at principal.
            SET @cover  = CASE WHEN @remaining >= @amt2 THEN @amt2
                               WHEN @remaining > 0 THEN @remaining ELSE 0 END;
            SET @frozen = CASE WHEN @rate2 IS NULL OR @rate2 <= 0
                                  OR DATEDIFF(day, @ed2, @rel) <= 0 THEN 0
                               ELSE ROUND((@amt2 - @cover) * (@rate2 / 100.0)
                                          * DATEDIFF(day, @ed2, @rel) / 30.0, 0)
                          END;

            UPDATE dbo.Entries
                SET Status = N'Released', ReleasedDate = @rel,
                    InterestCollected = @frozen,
                    AmountReturned = @amt2 - @cover + @frozen,
                    ModifiedAt = GETUTCDATE()
            WHERE Id = @id2;

            SET @principalBack += (@amt2 - @cover);
            SET @interestBack  += @frozen;
            SET @remaining     -= @cover;

            FETCH NEXT FROM done INTO @id2, @amt2, @rate2, @ed2;
        END
        CLOSE done; DEALLOCATE done;

        -- The two release legs, dated the release day.
        INSERT INTO dbo.CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
        VALUES (@rel, @principalBack, N'ReleasePrincipal', @gid, NULL, NULL, 1, @rel);
        INSERT INTO dbo.CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
        VALUES (@rel, @interestBack, N'ReleaseInterest', @gid, NULL, NULL, 1, @rel);

        -- TotalCollected (the counter's actual cash) rides on the primary entry.
        UPDATE p SET p.TotalCollected = @principalBack + @interestBack
        FROM dbo.Entries p
        WHERE p.Id = (SELECT TOP 1 Id FROM dbo.Entries
                      WHERE GroupId = @gid ORDER BY EntryDate, Id);
    END

    FETCH NEXT FROM g INTO @gIdx, @custId, @lend, @item, @amt, @nsub, @rel, @p1, @p1Amt, @p2, @p2Amt;
END
CLOSE g; DEALLOCATE g;

-- ── 5. Manual cash — movements outside the business ───────────────────────
INSERT INTO dbo.CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt) VALUES
('2025-11-01', 5000000, N'Manual', NULL, NULL, N'Starting capital for the counter', 1, '2025-11-01'),
('2026-01-02', 7500000, N'Manual', NULL, NULL, N'Partner capital tranche 2',        1, '2026-01-02'),
('2026-04-01', 7500000, N'Manual', NULL, NULL, N'Partner capital tranche 3',        1, '2026-04-01'),
('2026-07-01', 7500000, N'Manual', NULL, NULL, N'Partner capital tranche 4',        1, '2026-07-01'),
('2026-10-01', 7500000, N'Manual', NULL, NULL, N'Partner capital tranche 5',        1, '2026-10-01'),
('2025-12-05',   18500, N'Manual', NULL, NULL, N'Old shop fixtures sold',           1, '2025-12-05'),
('2026-01-09',  -12340, N'Manual', NULL, NULL, N'Shop shutter repair',              1, '2026-01-09'),
('2026-02-14',  260000, N'Manual', NULL, NULL, N'Partner brought in funds',         1, '2026-02-14'),
('2026-03-18',   -8750, N'Manual', NULL, NULL, N'Diwali bonus to helper',           1, '2026-03-18'),
('2026-04-21',   32300, N'Manual', NULL, NULL, N'Interest from personal FD',        1, '2026-04-21'),
('2026-06-30',  -15600, N'Manual', NULL, NULL, N'New CCTV + lockers',               1, '2026-06-30'),
('2026-08-11',  144800, N'Manual', NULL, NULL, N'Auction proceeds (old default)',   1, '2026-08-11'),
('2026-09-25',   -5290, N'Manual', NULL, NULL, N'Electricity + upkeep',             1, '2026-09-25'),
('2026-10-19',  227125, N'Manual', NULL, NULL, N'Partner quarterly top-up',         1, '2026-10-19');

COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

-- ── 6. Sanity checks — eyeball before opening the UI ──────────────────────
PRINT '';
PRINT '========== Rows inserted ==========';
SELECT (SELECT COUNT(*) FROM dbo.Customers)                      AS Customers,
       (SELECT COUNT(*) FROM dbo.Entries)                        AS LendRows,
       (SELECT COUNT(DISTINCT GroupId) FROM dbo.Entries)         AS Groups,
       (SELECT COUNT(*) FROM dbo.Entries WHERE Status = 'Released') AS ReleasedLends,
       (SELECT COUNT(*) FROM dbo.PartialReturns)                 AS Partials,
       (SELECT COUNT(*) FROM dbo.CashLedger)                     AS CashRows;

PRINT '========== Cash available (what the dashboard tile reads) ==========';
SELECT SUM(Amount) AS TotalCashAvailable FROM dbo.CashLedger;

PRINT '========== Released books — interest math spot check ==========';
SELECT TOP 6 e.GroupId, e.ItemDescription, e.AmountLend, e.InterestRate,
       e.ReleasedDate, DATEDIFF(day, e.EntryDate, e.ReleasedDate) AS DaysHeld,
       e.InterestCollected, e.AmountReturned, e.TotalCollected
FROM dbo.Entries e
WHERE e.Status = 'Released' AND e.TotalCollected IS NOT NULL
ORDER BY e.ReleasedDate DESC;

PRINT '========== Month by month (what the Summary charts plot) ==========';
SELECT FORMAT(LedgerDate, 'yyyy-MM')                                          AS [Month],
       SUM(CASE WHEN Source = 'Lend' THEN -Amount ELSE 0 END)                 AS [LentOut],
       SUM(CASE WHEN Source <> 'Lend' AND Amount > 0 THEN Amount ELSE 0 END)  AS [MoneyIn],
       SUM(Amount)                                                            AS [Net]
FROM dbo.CashLedger
GROUP BY FORMAT(LedgerDate, 'yyyy-MM')
ORDER BY [Month];

PRINT '========== Outstanding ranking (what the Customers chart plots) ==========';
SELECT TOP 10 c.Name,
       SUM(CASE WHEN e.Status = 'Active' THEN e.AmountLend ELSE 0 END)  AS [ActiveLent],
       COUNT(CASE WHEN e.Status = 'Active' THEN 1 END)                  AS [ActiveLends],
       (SELECT SUM(Amount) FROM dbo.PartialReturns pr
         WHERE pr.GroupId IN (SELECT GroupId FROM dbo.Entries e2
                               WHERE e2.CustomerId = c.Id AND e2.Status = 'Active')) AS [PartialsIn],
       SUM(CASE WHEN e.Status = 'Active' THEN e.AmountLend ELSE 0 END)
         - COALESCE((SELECT SUM(Amount) FROM dbo.PartialReturns pr
                     WHERE pr.GroupId IN (SELECT GroupId FROM dbo.Entries e2
                                           WHERE e2.CustomerId = c.Id AND e2.Status = 'Active')), 0) AS [ApproxOutstanding]
FROM dbo.Entries e
JOIN dbo.Customers c ON c.Id = e.CustomerId
GROUP BY c.Id, c.Name
ORDER BY [ApproxOutstanding] DESC;