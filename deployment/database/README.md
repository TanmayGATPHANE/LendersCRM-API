# deployment/database — SQL table + index models

Plain-SQL model of what the EF Core migrations create (`Data/Migrations/`),
for deployment on a server without the .NET SDK, or for reviewing the schema
in SSMS.

| File | Contents |
|---|---|
| `01-create-tables.sql` | `CREATE TABLE` for all five tables + the seeded admin account |
| `02-create-indexes.sql` | Every index (incl. the unique index on `Users.Email`) |

## How to deploy a fresh database

Either way produces the same schema:

1. **EF (preferred from a dev/build box):**
   `dotnet ef database update --project ../Data --startup-project .` —
   runs the migrations, which are the single source of truth.
2. **Plain SQL (server-only):** run `01-create-tables.sql`, then
   `02-create-indexes.sql` against the empty database.

## Rules these files follow

- Money is `DECIMAL(12,2)`, rates `DECIMAL(5,2)`. Never float/double.
- Entry dates are `DATE` (no time-of-day); `CreatedAt` audits are `datetime2` UTC.
- There are **no cross-table foreign keys** except `Entries.CustomerId →
  Customers.Id` (as the EF migration created it). Every other relation
  (`Entries.GroupId`, `PartialReturns.GroupId`, `CashLedger.GroupId/ReferenceId`,
  `*_UserId` columns) is **logical** — enforced by the app layer, kept loose so
  corrections/edits never fight the database.
- `CashLedger` is the Total-Cash-Available table: every rupee in hand is a
  stored row; `SUM(Amount)` is the balance. Release posts two rows
  (principal + interest); partial returns post one; manual ± entries post one.
  Only `Source = 'Manual'` rows may be deleted.

When the schema changes, update the EF migration first, then mirror the
change here in the same commit.