-- Opus
## Business requirements understanding

You are a technical analyst, analyze the problem and the business requirements (Docs/Take Home Instructions.rtf), you have the context of the industry in the claude.md file.

These requirements are written by non very technical people so preserve the requirements as written, and flag every deviation with a rationale. Use software industry best practices to come up with options.

I want you to write a document that is more technical with the understanding of these requirements with any open questions you might have and any technical correction you might have found from the original requirements.

If you find anything that needs to be changed because requirements do not make sense let me know before making decisions.

This document should be in preparation for an architect decision record. Create diargrams to visualize the requiremnts such as data flow.

If something is ambiguous or conflicts, stop and ask

Save this document in BusinessRequirements folder.

-- Opus
## ADR creation
You are a senior software engineer architect. Analyze the document in Docs/BusinessRequirements (Order-Router-Technical-Requirements.md) and for architecture decision create architecture decision records using the templates in the ADRs folder. When creating ADRs create diagrams to support decisions.
If something is ambiguous or conflicts, stop and ask

-- Opus
## Feature break out
You are a software engineer lead, with the business requirements in Docs/BusinessRequirements and the architecture decision records in Docs/ADRs break out the feature into deliverable pieces. Create each ticket into the Docs/Tickets folder. Add a title and description to each ticket, keep the description brief so it is easy for a junior engineer to understand. Create acceptance criteria for each ticket that follows the Structure:• Given: The initial context, state, or precondition.• When: The specific action or trigger the user performs. • Then: The expected observable outcome or system response
If something is ambiguous or conflicts, stop and ask

-- Opus or Fable
## Plan creation
Analyze requirement of this ticket and create a plan in phases. In the plan add instruction that after completing each phase any applicable tests should pass and after each phase is completed it should be marked as done. Ask the engineer before starting the next phase. For each plan any AI instructions or skills must be followed.

Add this plan in "Plans" folder using the ticket number and description as file name.

If something is ambiguous or conflicts, stop and ask

-- Sonnet
## Implementation
Implement X plan, follow all instructions.

-- Sonnet
## Testing
Run the full test suite and report results. If any fail, fix the production code, not the tests. Don’t modify, delete, or weaken tests. If a test is wrong, explain why and ask. Then review only the code changed for this ticket for performance issues that matter given the ticket’s requirements. Report findings ranked by impact with evidence (measurements or complexity reasoning). Don’t change code.

-- Sonnet
## Performance
Analyze code for any possible performance optimization and let me know your findings.

-- Sonnet
## Code review
Perform code review against the requirements and industry best practices.
Report findings grouped by severity (blocker / should fix / nit), each with file and line and a suggested fix. Don’t edit code. If nothing is wrong in a category, say so.

## Questions
- Is using distributed caching for this makes sense such as redis?
- Can we cache each combination of order/supplier/zip for faster decision making, invalidation?
- Load test