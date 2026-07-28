# Leave approval hierarchy — known issues

This documents the leave-approval routing system implemented on 2026-07-28
(`LeaveApprovalPolicy` + the rewritten `ManagerContext.DecideRequestAsync` /
`EmployeeContext.HrDecideRequestAsync` / `EmployeeContext.SubmitRequestAsync`) and a
list of known gaps/edge cases it does **not** currently handle. Written to hand off to
a future session to design and implement fixes — nothing in the "Issues" section below
has been fixed yet.

## What the system does today

`src/CompanyEmployees.Domain/LeaveApprovalPolicy.cs` decides who must approve a given
user's leave request, based on `User.Role`, `User.Department`, and `User.Manager?.Role`
(one level up only — see Issue 1):

- **Admin** → nobody; auto-approved the moment it's submitted
  (`EmployeeContext.SubmitRequestAsync`), never enters `Pending`.
- **HR-department staff who are not themselves the HR LineManager** → their manager
  only. No HR-team review (would otherwise mean HR approving HR).
- **Everyone else whose manager's `Role` is `LineManager`** → that manager **and**
  HR both required.
- **Everyone else whose manager is not a LineManager** (no manager, or the manager is
  an Admin) → HR only.

Each required approval is recorded as a `LeaveApproval` row (`Step = 1` for the
manager, `Step = 2` for HR — constants on `LeaveApproval`). A **reject is final
immediately**, regardless of whether the other side has weighed in. An **approve only
finalizes the request** (`LeaveRequest.Status = Approved`) once every required side has
an `Approved` row; otherwise the request silently stays `Pending`, waiting on the other
approver. `LeaveApprovalPolicy.IsFullyApproved` is the check for this.

`LeaveRequestRepository.GetPendingRequestsByManagerAsync` /
`GetAllPendingRequestsAsync` each exclude requests where *that* side has already
decided, so a manager or HR reviewer only ever sees items still needing *their* action.

Verified (2026-07-28) with a throwaway console harness exercising the real
`EmployeeContext`/`ManagerContext` against the dev DB — not part of the repo, not a
persisted test suite. See Issue 9.

## Issues

### 1. Manager-chain cycles are not detected or prevented

`User.ManagerId` is a self-referencing FK (`DeleteBehavior.NoAction`) with **no
cycle check anywhere** — not a DB constraint, not application-level validation. Nothing
stops `A.ManagerId = B` and `B.ManagerId = A` (or a longer cycle) from existing.

Today this is **not user-triggerable**: no UI in the app currently lets anyone edit a
*user's* `ManagerId` (`AdminUsers.razor` only reassigns `DepartmentId`;
`AdminDepartments.razor`'s manager picker sets `Department.ManagerId`, a different FK —
which LineManager heads a department, not an individual's reporting line). `ManagerId`
is only ever set by the seed migrations today.

It's a landmine for the next feature that touches it, though — "reassign an employee's
manager" is an obvious next admin feature given `AdminUsers` already exists, and:

- `LeaveApprovalPolicy.DetermineRequirement` and `EmployeeContext.GetTeamMembersAsync`
  each only walk **one level** up (`requester.Manager`), so a cycle would not currently
  cause a live infinite loop or stack overflow in this code.
- But it *would* silently produce nonsense: two LineManagers approving each other's
  leave, "team" membership that doesn't reflect any real org chart, etc. And any future
  feature that walks the *full* chain (org chart view, skip-level escalation, "how many
  reports does X have transitively") would need cycle-safe traversal from day one, or it
  will infinite-loop the first time someone (accidentally or via a bug elsewhere)
  creates one.

**Needs**: a cycle check wherever `ManagerId` ever becomes settable (reject the write if
walking up from the new manager reaches back to the user being reassigned), and/or a
depth-limited or visited-set-guarded traversal in any future full-chain walk.

### 2. `HrDecideRequestAsync` has no server-side check that the approver is actually HR

`ManagerContext.DecideRequestAsync` enforces `request.User.ManagerId == managerId`
server-side — the caller can't approve someone else's report even if the UI is
bypassed. `EmployeeContext.HrDecideRequestAsync` has **no equivalent check on the
approver**: it trusts that only HR staff can reach it, because `/hr/dashboard` gates
entry on the `Department` claim. Any authenticated user who calls the underlying method
directly (or if the department claim were ever stale/spoofable) could decide **any**
HR-required request. This mirrors a pre-existing gap (the original single-approval
`HrDecideRequestAsync` never validated the approver either) — it just wasn't rewritten
to add the check when the rest of the method was.

**Needs**: load the approver's own `Department` server-side and verify it's `"HR"`
before allowing the decision (same pattern as the manager-ownership check).

### 3. The approval requirement is re-evaluated live at decision time, not frozen at submission

`DetermineRequirement(request.User)` is called fresh inside `DecideRequestAsync` and
`HrDecideRequestAsync` — it reads whatever `request.User`'s `Role`/`Department`/
`Manager` are **at the moment of the decision**, not what they were when the request
was submitted. If a user's role, department, or manager changes while they have a
request sitting `Pending`, the approval rule that applies can change out from under an
in-flight request — e.g. a request created as "needs manager + HR" could, after a role
change, suddenly only need HR, or vice versa. There's no snapshotting of which rule
applied at submission time.

**Needs**: either an explicit product decision that "current state always governs" is
fine, or capture the requirement (or the inputs to it) on the `LeaveRequest` at
submission time and use that snapshot for all later decisions.

### 4. A stale approval survives manager reassignment

Related to #3: `IsFullyApproved` checks "does *some* `Step = ManagerApprovalStep,
Approved` row exist," not "does an approval from the requester's *current* manager
exist." If a request is manager-approved by manager A, and the requester's manager is
then reassigned to B before HR also approves, A's now-stale approval still counts —
B was never asked and has no way to weigh in or veto.

**Needs**: decide whether a manager change on a request already partially approved by
the *old* manager should invalidate that approval and require the new manager to
re-decide.

### 5. Concurrent double-decision race — no DB constraint stops two rows for the same step

`LeaveApprovalConfiguration` only defines `HasKey(la => la.Id)` — there is **no unique
index on `(LeaveRequestId, Step)`**. The "have I already decided this?" guards
(`request.Approvals.Any(a => a.Step == ...)`) are check-then-act with no database-level
backstop: two concurrent decisions on the same step (e.g. two HR staff both clicking
Approve within the same window, or a double-click/double-tab) can both pass the
in-memory check before either commits, producing two `LeaveApproval` rows for the same
step on the same request.

**Needs**: a unique index on `(LeaveRequestId, Step)` (would need a migration) so the
second concurrent write fails at the DB and can be handled explicitly, instead of
silently succeeding twice.

### 6. Admin auto-approval leaves no audit trail

When `LeaveApprovalPolicy.DetermineRequirement` returns `AutoApproved = true` (the
requester is an Admin), `SubmitRequestAsync` creates the `LeaveRequest` directly as
`Approved` with **zero `LeaveApproval` rows** — there's no record of who approved it or
when, because nothing did. `DbTimeOffService.MapRequest`'s `DecidedBy`/`DecidedAt`
mapping (`Approvals.Where(a => a.ReviewedAt != null).OrderByDescending(a =>
a.Step).FirstOrDefault()`) will find nothing, so an Admin's own approved request shows
no "Decided by" info in `RequestDetailsDialog` even though `Status == Approved`. Not
visually broken (the dialog only renders that line at all) but worth a maintainer
knowing this is deliberate, not a missed case.

**Needs**: either accept this as fine ("auto-approved" is self-explanatory), or write a
synthetic `LeaveApproval`/audit row (with no real `ApproverId`, or a system marker) so
there's a persisted record of *when* it was auto-approved.

### 7. No visibility into which side a partially-approved request is still waiting on

While a dual-approval request has one side done and the other outstanding, the
requester's `/employee/my-requests` page just shows `Pending` — same as a request
nobody has touched yet. There's no "awaiting HR" vs "awaiting your manager" signal
anywhere in the UI (only the two dashboards' pending-list filtering reflects it,
indirectly, by whether the item is still in *their* list).

**Needs**: a product decision on whether this is worth surfacing, and if so, where
(e.g. a computed label on `TimeOffRequest` derived from `Approvals`).

### 8. The policy only ever requires the *direct* manager, never the full chain

By design (matches the 4 business rules as given — each says "his linemanager,"
singular), `DetermineRequirement` never requires approval from anyone above the direct
manager, no matter how deep the org chart gets. If the org structure grows a third
level (LineManager reporting to LineManager reporting to LineManager), only the
requester's immediate manager is asked — not documented as wrong, just worth
re-confirming is still the intended behavior if the org shape changes.

### 9. No persisted automated test coverage

Verification for all of the above logic was a throwaway console harness
(`ApprovalCheck` — not committed, lived in a temp scratch directory, deleted its own
test data after running) exercising `EmployeeContext`/`ManagerContext` against the real
dev DB. It's gone now. There is still no test project in this repo (`CLAUDE.md`: "No
test project. When one lands, wire up `dotnet test`"). This approval-routing logic —
several branches, two independent decision entry points that both have to agree, a
partial-approval state machine — is exactly the kind of thing that regresses silently
without a real, repeatable test suite.
