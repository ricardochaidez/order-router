# OR-002: Model suppliers, products and ZIP coverage in the domain

**Depends on:** [OR-001](OR-001-solution-skeleton.md)  
**References:** [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §3, §4.4, §5.1, FR-09, DQ-04, DQ-06, DQ-09, SC-07, SC-09

## Description

Create the domain types that the routing rules use: `Category`, `ZipCode`, `ZipRange` / `ServiceArea`, `Supplier` and `Product`. These are plain domain objects with no database code. `Category` normalizes its value (trim + lower-case) so casing bugs can't happen. `Supplier` can answer three questions: *do you handle this category?*, *do you serve this ZIP?* and *are you mail-only?*

## Acceptance criteria

### AC1: Category ignores case and spaces

- **Given** two categories created from `" CPAP "` and `"cpap"`
- **When** I compare them
- **Then** they are equal and have the same hash code

### AC2: Empty category is rejected

- **Given** an empty or whitespace category value
- **When** I create a `Category`
- **Then** creation fails with a clear error

### AC3: ZIP code is exactly 5 digits

- **Given** the values `"02130"`, `"2130"` and `"0213A"`
- **When** I create a `ZipCode` from each
- **Then** only `"02130"` is accepted

### AC4: ZIP ranges are inclusive

- **Given** a supplier serving `02114-02212`
- **When** I ask if it serves `02114`, `02212` and `02213`
- **Then** the answers are yes, yes, no

### AC5: Mail-only supplier

- **Given** a supplier with `can_mail_order = y` whose service area is exactly the configured nationwide range `00100-99999`
- **When** I ask whether it is mail-only
- **Then** the answer is yes

### AC6: Nationwide local-only supplier is not mail-only

- **Given** a supplier with `can_mail_order = n` and service area `00100-99999`
- **When** I ask whether it is mail-only
- **Then** the answer is no (SC-07)

### AC7: Unrated supplier

- **Given** a supplier with no rating
- **When** I read its rating
- **Then** the rating is empty (null), not zero

### AC8: Rating out of range

- **Given** a rating of `0` or `11`
- **When** I create a supplier
- **Then** creation fails

### AC9: Nationwide range is configurable

- **Given** `NationwideZipRange` configured as `00500-99999`, a mail-capable supplier serving exactly `00500-99999` and another serving exactly `00100-99999`
- **When** I ask whether each is mail-only
- **Then** the first is mail-only and the second is not (FR-09)
