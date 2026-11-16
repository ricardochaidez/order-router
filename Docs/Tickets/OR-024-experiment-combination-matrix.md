# OR-024: Test all eight experiment arm combinations

**Depends on:** [OR-017](OR-017-exp1-bounded-tradeoff.md), [OR-018](OR-018-exp2-cold-start-prior.md), [OR-019](OR-019-exp3-partial-routing.md), [OR-020](OR-020-dependency-warnings.md), [OR-023](OR-023-acceptance-tests.md)  
**References:** [ADR-0001](../ADRs/ADR-0001-routing-strategy-composition.md) (Negative consequences)

## Description

The three experiments can be combined in 2 × 2 × 2 = 8 ways. Add one test per combination on sentinel data to catch surprising interactions (for example, the cold-start prior together with the bounded trade-off).

## Acceptance criteria

### AC1: Every combination covered

- **Given** a fixed assignment for each of the 8 arm combinations and a fixed random source
- **When** the same test orders are routed
- **Then** each combination returns its expected, documented result

### AC2: All control equals spec

- **Given** every experiment in control
- **When** the sentinel orders are routed
- **Then** the results equal those of the acceptance suite in OR-023

## Notes

- Arms are pinned per test host through a temp `experiments.json` (`ExperimentArmsHost`, reusing the shared `TempExperimentsFile`): treatment is enabled at 100% with no targeting, control is disabled. A `FixedRandomSource` replaces `IRandomSource` in DI. Diagnostics are on so each test checks the arms it got.
- Orders: A `00007` wheelchair + cane (EXP-1), B `00050` rollator (EXP-2), C `00098` wheelchair + walker (EXP-3), D `00051` wheelchair + cane (EXP-1 x EXP-2 interaction). The expected result per combination is in the `ExperimentCombinationMatrixTests` docs.
- The sentinel seed has no rated competitor for the unrated suppliers, so B and D use five test-only suppliers (`SUP-X001`, `SUP-X002`, `SUP-X011` to `SUP-X013`) in ZIPs `00050` and `00051`. `MatrixDatabaseFixture` adds them to a throwaway database seeded with the real files; `Docs/Data/Seed` is untouched.
- Interaction found: EXP-2 treatment lifts the one-shipment baseline from 0 to 6.5, which can drop EXP-1 treatment's uplift below the threshold (order D, combinations 5 and 6 against 7 and 8). Expected, no defect.
- AC2: `AllControlEqualsAcceptanceTests` checks the OR-023 orders with an explicit all-control file and with the shipped file. The expectations live in `Or023Expectations`, which the OR-023 acceptance tests now also read.
- OR-020 is not implemented (no `DEPENDENT_ITEM_UNFULFILLED` in `src/`). The matrix was built without dependency warnings; re-run it when OR-020 lands.
