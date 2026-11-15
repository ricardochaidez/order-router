# OR-003: Parse and normalize the supplier and product CSV files

**Depends on:** [OR-002](OR-002-domain-reference-model.md)  
**References:** [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §6 (DQ-01 to DQ-12), NFR-04, NFR-10; [ADR-0002](../ADRs/ADR-0002-reference-data-database.md)

## Description

Write pure functions that read `suppliers.csv` and `products.csv` and turn each row into a clean record, applying the data-quality rules in §6. The files are messy: 4-digit ZIPs, lists mixed with ranges, mixed category casing, a misspelled header, and duplicate products. No database work here, so this is easy to unit-test.

The source files in `Docs/Data` (`suppliers.csv`, `products.csv` and `sample_orders.json`) are **read-only inputs**. Never edit, reformat or overwrite them. Write the normalized output to **new files** in `Docs/Data/Seed/`: `suppliers.normalized.csv` and `products.normalized.csv`. The seeder ([OR-005](OR-005-reference-data-seeder.md)) loads these files.

This normalization is the **only** place the §6 rules are applied. It works as a generator: you run it when the raw CSVs change, and the generated files are committed to the repository. Neither the seeder nor API startup runs it. Because the seeder only ever sees clean rows, this generator owns all data-quality reporting: skipped rows with reasons, and duplicate products.

Put the generator in its own console project, `tools/OrderRouter.DataNormalizer` (references Domain only), so it ships with neither the API nor the tests. Run it with `dotnet run --project tools/OrderRouter.DataNormalizer`; by default it reads `Docs/Data` and writes `Docs/Data/Seed`. Unit tests call its pure functions directly.

## Acceptance criteria

### AC1: Leading zeros restored

- **Given** a supplier row with service ZIPs `2130, 2114-2212`
- **When** the row is parsed
- **Then** the coverage is `02130` and the range `02114-02212`

### AC2: Lists and ranges mixed

- **Given** a row with `10059-10103, 90117, 60699`
- **When** the row is parsed
- **Then** it produces one range and two single ZIPs

### AC3: Ratings parsed safely

- **Given** rating values `10`, `10.0`, `6` and `no ratings yet`
- **When** the rows are parsed
- **Then** they become 10, 10, 6 and empty (unrated)

### AC4: Header typo accepted

- **Given** a file whose name column is `suplier_name`, and another where it is `supplier_name`
- **When** each file is parsed
- **Then** the supplier name is filled in for both

### AC5: Mail-order flag

- **Given** `can_mail_order?` values `y`, `Y` and `n`
- **When** the rows are parsed
- **Then** they become true, true and false

### AC6: Supplier IDs unchanged

- **Given** the IDs `SUP-005`, `SUP-0810` and `SUP-T003`
- **When** the rows are parsed
- **Then** the IDs are kept exactly as written

### AC7: Identical duplicate products

- **Given** two identical rows for `HB-MAN-019-BAS`
- **When** the products file is parsed
- **Then** one product is kept and a warning is logged

### AC8: Conflicting duplicate products

- **Given** two rows for the same product code with different categories
- **When** the products file is parsed
- **Then** parsing fails with an error naming the product code

### AC9: Bad rows skipped

- **Given** a row with an unparseable rating
- **When** the file is parsed
- **Then** that row is skipped, the row number and reason are logged, and the other rows are kept

### AC10: Normalized files written

- **Given** the source `suppliers.csv` and `products.csv` in `Docs/Data`
- **When** normalization runs
- **Then** `Docs/Data/Seed/suppliers.normalized.csv` and `Docs/Data/Seed/products.normalized.csv` are created, containing only the valid, normalized rows (padded ZIPs, lower-case categories, parsed ratings, de-duplicated products)

### AC11: Source files unchanged

- **Given** the files in `Docs/Data` (`suppliers.csv`, `products.csv`, `sample_orders.json`)
- **When** normalization runs
- **Then** their content is byte-for-byte unchanged

### AC12: Normalization summary

- **Given** a completed normalization run
- **When** I read the logs
- **Then** there is one summary with rows read, rows written, rows skipped (with row numbers and reasons) and duplicate products removed, for each file

### AC13: Output is repeatable

- **Given** unchanged source files
- **When** normalization runs twice
- **Then** the generated files are identical (same rows, same order), so regenerating produces no diff

### AC14: One documented command

- **Given** a fresh clone
- **When** I run `dotnet run --project tools/OrderRouter.DataNormalizer`
- **Then** the files in `Docs/Data/Seed/` are regenerated and the summary is printed
