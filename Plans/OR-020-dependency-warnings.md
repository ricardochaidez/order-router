# Plan: OR-020 — Descope "Warn when a routed item needs an unfulfilled one"

**Ticket:** Docs/Tickets/OR-020-dependency-warnings.md
**References:** Requirements FR-18, §8.4 (DR-01 to DR-05), SC-08, SC-11; `CLAUDE.md`; `.claude/rules/csharp.md`; `.claude/rules/dotnet-ddd-architecture.md`; `.claude/skills/ddd-implementation`
**Plan file location (once approved):** `Plans/OR-020-dependency-warnings.md`

## Context

The engineer decided to **remove OR-020 from this repo**. The ticket would have loaded dependency rules from configuration and added `DEPENDENT_ITEM_UNFULFILLED` warnings on partial routes. The ticket is therefore not built; this plan removes it and every statement that promises it.

### Decisions confirmed with the engineer

| Topic | Decision |
|---|---|
| Scope | Whole ticket is removed (not just the config-location choice). |
| Depth | **Docs + ticket only.** The always-empty `warnings: []` field stays in the v1 response, because CLAUDE.md allows only additive changes within a version. No behaviour change. |
| Earlier answer | The "new read port for unknown categories" answer for AC4 is moot: AC4 goes away with the ticket. |

### Items to resolve during the work (stop and ask if unclear)
- SC-08 was a stakeholder decision ("Tell the user"). Descoping it is recorded as a new deviation/decision entry, not a silent delete.
- Engineer asked (after Phase 0) to remove OR-020 from all other plans and ADRs. Done: `Plans/OR-015`, `Plans/OR-019`, ADR-0001, ADR-0004 no longer mention OR-020, FR-18 or dependency warnings.

## Working rules (apply to every phase)

1. **Follow the AI instructions and skills.** Read `CLAUDE.md`, `.claude/rules/csharp.md` and `.claude/rules/dotnet-ddd-architecture.md` first. The `ddd-implementation` skill's domain analysis and review are N/A here (no domain or layer change); say so in the final report. Any C# touched is comment-only and follows `.editorconfig` and XML-doc rules.
2. **Tests must pass after each phase.** Run `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` from the repo root; all must succeed with 0 errors. Doc-only phases still run them to prove nothing broke.
3. **Mark each phase as done.** Change its heading from `⬜ Not started` to `✅ Done` in `Plans/OR-020-dependency-warnings.md` and tick its checklist.
4. **Ask before the next phase.** After marking a phase done, stop, summarize what changed and the test results, and ask the engineer for approval before starting the next phase.
5. **Stop and ask if something is ambiguous or conflicts.**
6. No commits unless asked; if asked, branch off `main` first (e.g. `docs/OR-020-descope`).

---

## Phase 0 — Save the plan ✅ Done
- [x] Create `Plans/OR-020-dependency-warnings.md` with this plan.

**Verify:** file exists; no code changed.

## Phase 1 — Requirements document ⬜ Not started
File: `Docs/BusinessRequirements/Order-Router-Technical-Requirements.md`
- [ ] FR-18 (line ~278), §8.4 DR-01..DR-05 (~435-445), SC-08 (~82), SC-11 (~101), DEV-11 (~630), OQ-07 (~647), OQ-15 (~669): mark as descoped using the doc's existing strike-through / "resolved" convention, with the reason "descoped by engineer decision" and a pointer to the new entry.
- [ ] Add one deviation/decision entry recording the descope and that `warnings` stays as an always-empty array for contract stability (ADR-0004).
- [ ] Remove the dependency step from the EXP-3 diagram (~212) and the EXP-3 treatment text (~401); remove the "Item dependency" glossary entry (~124); fix the DR-01 reference in the coding guideline (~287).
- [ ] §9: `warnings` row (~477) says "always empty in v1"; remove the hypothetical `DEPENDENT_ITEM_UNFULFILLED` example (~508-519).

**Verify:** `grep -n -E "FR-18|DR-0|DEPENDENT_ITEM"` shows only descoped markers; `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` pass.

## Phase 2 — ADRs and code comments ⬜ Not started
- [x] ADR-0001 and ADR-0004 mentions removed (done early at the engineer's request); the remaining ADR-0004 note that `warnings` is reserved and always empty is still open.
- [ ] Comment-only edits: `src/OrderRouter.Api/ViewModels/Response/RouteResponseViewModel.cs:52` and `RoutingWarningResponseViewModel.cs:3` (stop citing FR-18; say reserved, always empty). No signature or behaviour change.

**Verify:** build, tests (incl. `RouteResponseTests`, `RoutingOrderMappingExtensionsRouteTests`, OpenAPI tests) and format pass.

## Phase 3 — Remove the ticket and links ⬜ Not started
- [ ] Delete `Docs/Tickets/OR-020-dependency-warnings.md` (`git rm`).
- [ ] `Docs/Tickets/README.md`: remove the OR-020 row (line 26), remove OR-020 from OR-024's Depends-on (line 30), remove the DR-05 bullet (line 47).
- [ ] `Docs/Tickets/OR-024-experiment-combination-matrix.md` (line 3): drop the OR-020 dependency; check its body for dependency-warning expectations and ask if any exist.
- [ ] Do **not** renumber other tickets.

**Verify:** `grep -rn "OR-020" Docs` finds only the descope record; links resolve; build, tests, format pass.

## Phase 4 — Final review ⬜ Not started
- [ ] Repo-wide grep for `OR-020|FR-18|DR-0[1-5]|DEPENDENT_ITEM|dependencies.json` (excluding `Plans/` history) is clean.
- [ ] Report honestly: no production behaviour changed; `ddd-implementation` checks N/A; `warnings: []` kept.

**Verify:** full `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` green.
