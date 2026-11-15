# OR-010: Find the eligible suppliers for each category

**Depends on:** [OR-002](OR-002-domain-reference-model.md), [OR-006](OR-006-reference-data-read-path.md)  
**References:** [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §7.1, FR-06, FR-07, FR-08, FR-09, SC-06, SC-07, SC-12, §9.4 reason codes

## Description

Write the domain rules that decide which suppliers may fulfill a category for an order, and whether each would ship `local` or `mail`. These are fixed feasibility rules; no experiment may change them. If no supplier is eligible, return the right reason code.

## Acceptance criteria

### AC1: Local order

- **Given** `mail_order = false` at ZIP `00001`, a `wheelchair` item
- **When** eligibility runs
- **Then** only suppliers that handle `wheelchair`, serve `00001` and are not mail-only are eligible (`SUP-T001`)

### AC2: Mail-only excluded from local orders

- **Given** `mail_order = false` and a mail-only supplier that handles the category
- **When** eligibility runs
- **Then** the mail-only supplier is not eligible (SC-12)

### AC3: Nationwide local-only supplier

- **Given** `mail_order = false`, a supplier with `00100-99999` and `can_mail_order = n`
- **When** eligibility runs for a ZIP inside the range
- **Then** the supplier is eligible as `local` (SC-07)

### AC4: Mail order

- **Given** `mail_order = true`
- **When** eligibility runs
- **Then** only suppliers with `can_mail_order = y` are eligible, whatever the ZIP, and `SUP-T001` (no mail) is excluded (SC-06)

### AC5: Fulfillment mode

- **Given** eligible suppliers
- **When** the mode is computed
- **Then** mail-only suppliers are `mail`; others are `local` if they serve the ZIP and `mail` if they don't

### AC6: No supplier, local order

- **Given** `mail_order = false` at `00098` and a category other than `wheelchair`
- **When** eligibility runs
- **Then** the item is unfulfillable with `NO_SUPPLIER_FOR_CATEGORY_IN_AREA`

### AC7: No supplier, mail order

- **Given** `mail_order = true` and a category that no mail-capable supplier handles
- **When** eligibility runs
- **Then** the item is unfulfillable with `NO_SUPPLIER_FOR_CATEGORY`
