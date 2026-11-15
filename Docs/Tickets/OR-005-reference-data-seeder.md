# OR-005: Seed reference data by upsert from the integration tests

**Depends on:** [OR-003](OR-003-csv-normalization.md), [OR-004](OR-004-database-schema.md)  
**References:** [ADR-0002](../ADRs/ADR-0002-reference-data-database.md) (Seeding and migrations); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) FR-04, SC-21, SC-22, SC-24, OQ-24, NFR-04, NFR-08

## Description

Build `ReferenceDataSeeder` in Infrastructure. It applies the migrations, then **upserts** the normalized CSV data in one transaction. It reads the normalized files that [OR-003](OR-003-csv-normalization.md) writes to `Docs/Data/Seed/` (`suppliers.normalized.csv`, `products.normalized.csv`), never the raw source files in `Docs/Data`, and it never modifies any data file.

The seeder does **no normalization**: no ZIP padding, no category casing fixes, no de-duplication. It builds the [OR-002](OR-002-domain-reference-model.md) domain objects from each row, and that step acts as validation. Clean files pass. Any row the domain rejects means the file is dirty or was edited by hand, so the seed fails fast. It never fixes or skips such a row. Only the integration-test fixture calls it, once before the tests run. Neither API startup nor `docker compose up` ever seeds. The fixture uses the same connection string as the API, so running the tests loads the API's data.

## Acceptance criteria

### AC1: First seed

- **Given** an empty database
- **When** I run the integration tests
- **Then** migrations are applied and every supplier and product from the normalized files in `Docs/Data/Seed/` is in the database, including the sentinel suppliers `SUP-T001` to `SUP-T014`

### AC2: Seed is idempotent

- **Given** a database that was already seeded
- **When** I run the integration tests again
- **Then** the row counts are the same and there are no duplicates

### AC3: Changed supplier is updated

- **Given** a supplier whose categories or ZIPs changed in the CSV
- **When** the seed runs
- **Then** that supplier's categories and service areas match the CSV exactly (old child rows replaced)

### AC4: Removed rows are kept

- **Given** a supplier in the database that is no longer in the CSV
- **When** the seed runs
- **Then** the supplier is not deleted, and the summary log reports how many such rows exist

### AC5: Missing file fails fast

- **Given** a missing `suppliers.normalized.csv` path
- **When** the seed runs
- **Then** the seed fails with a clear message and the test run fails

### AC6: Summary log

- **Given** a completed seed
- **When** I read the logs
- **Then** there is one summary with rows loaded, unrated suppliers, and rows in the database that are no longer in the CSV (skipped rows and duplicates are reported by [OR-003](OR-003-csv-normalization.md) when the files are generated)

### AC7: No migrations or seeding elsewhere

- **Given** an empty database
- **When** I start the stack with `docker compose up` and start the API
- **Then** no migrations are applied and nothing is seeded: the reference and order tables do not exist until the integration tests run (SC-33)

### AC8: Invalid normalized row fails the seed

- **Given** a normalized file containing a row the domain rejects (e.g. a 4-digit ZIP or a rating of `11`)
- **When** the seed runs
- **Then** the seed fails with an error naming the file, row number and reason, nothing is written (the transaction rolls back) and the test run fails
