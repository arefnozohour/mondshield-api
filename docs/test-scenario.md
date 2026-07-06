# MondShield — full end-to-end test scenario

A manual QA walkthrough exercising the whole system (backend + frontend) through the complete
trader lifecycle. Last run: **all steps pass**.

## Prerequisites

1. **MT5 in Stub mode** — the scenario provisions an account, and Stub simulates MT5 perfectly
   with no live-server friction. In `src/MondShield.Api/appsettings.json` (and
   `appsettings.Local.json` if present) set `"Mt5": { "Mode": "Stub" }`.
   *(To exercise the real Manager API instead, set `Mode: Live` + the server/manager secrets —
   but provision one account at a time and shut the API down gracefully; see `mt5-integration.md`.)*
2. **Clean DB** (optional but recommended for a repeatable run) — set
   `Database:RecreateOnStartup=true` in `appsettings.Development.json` so each run starts fresh and
   re-seeds. Set back to `false` when you want data to persist.
3. **Both servers running:**
   - Backend: `dotnet run --project src/MondShield.Api --launch-profile http` → http://localhost:5259
   - Frontend (in `mondshield-web`): `npm run dev` → http://localhost:3000

## Seeded accounts (from `appsettings.Development.json` → `Seed`)

| Role  | Email                     | Password     |
|-------|---------------------------|--------------|
| Admin | admin@mondshield.local    | Admin#12345  |
| User  | user1@mondshield.local    | User#12345   |
| User  | user2@mondshield.local    | User#12345   |

Seed users start at **PendingKyc**. This scenario registers a *fresh* trader to also cover signup.

---

## The scenario

### 1. Register a new trader
- Go to http://localhost:3000 → **Create an account**.
- Full name `Scenario Trader`, email `scenario@test.local`, password `Scenario#123`, tick the
  agreement, **Create account**.
- ✅ **Expected:** lands on **/onboarding** showing *"Application under review"* with the journey
  rail (KYC review → MT5 setup → Fund account → Live) and the first step active.

### 2. Admin approves KYC → provisions MT5 → activates
- Sign out / open a second session and sign in as **admin**.
- **Worklist** → the onboarding queue lists `Scenario Trader · scenario@test.local · PendingKyc`.
- Click **Approve KYC**. Then on the row (now *KycApproved*) the MT5 provisioning form appears —
  name/email are pre-filled → **Provision**.
  - ✅ **Expected:** returns an MT5 login (Stub: `1000001`) + generated passwords; status →
    *Provisioned*.
- Row now *Provisioned* → enter deposit `2000` → **Confirm deposit**.
  - ✅ **Expected:** status → **Active**; the account is at **Stage 1** with **$2,000 insured
    capital**.

### 3. Trader dashboard
- Sign in as `scenario@test.local`.
- ✅ **Expected:**
  - Top bar balance **$2,000.00**; balance composition: Insured capital **$2,000**, Compensation
    /Profit/Commission **$0**; MT5 login shown.
  - **Loss coverage 50%**, **Broker share 30%** (Stage 1 rates).
  - Stage ladder with **"YOU"** on Stage 1.

### 4. Submit a compensation claim
- **Claim** (Compensation) → Total trading loss `600`, Commission paid `40`.
- ✅ **Expected (live preview):** Estimated payout **$280.00** — i.e. 50% × ($600 − $40) = $280.
- **Submit compensation request**.
- ✅ **Expected:** the status tracker appears — **Submitted** (dated), Under review, Approved, Paid.

### 5. Admin reviews & approves the claim
- As admin → open the compensation review (Worklist → review queue row, or
  `/admin/compensation/{id}`).
- ✅ **Expected:** trader identity (**Scenario Trader**, email, account id, MT5 login, Stage 1);
  coverage calc — Cycle loss −$600, Commission −$40, **Eligible loss $560**, Coverage rate 50%,
  **Payout $280**; lifetime cap **$0 of $5,000 used**.
- State-aware buttons: only **Start review** shows (Submitted). Click it → now **Approve payout
  $280.00** / **Reject request** show.
- Click **Approve payout**.
- ✅ **Expected:** *"Payout approved — $280.00 scheduled for Jul 27"* (the next 27th–28th batch).
  No "Undo" button (there's no backend revert — by design).

### 6. Trader history (unified activity feed)
- As the trader → **History**.
- ✅ **Expected:** one row — **Deposit +$2,000.00 · "Stage 1 activation deposit"**. The
  compensation is **not** here yet: it's approved but not *paid* until the payout job runs on the
  27th, and the feed shows settled ledger facts only (pending items show on the Compensation page).

### 7. Withdraw profit
- **Withdraw**.
- ✅ **Expected:** **Withdrawable profit $0.00** with the note *"Capital and compensation are not
  withdrawable as profit."* — correct, because trading profit only accrues from real MT5 trades
  (out of scope for this build). With real profit present, the live preview would show
  gross / broker-share / net, and submitting creates a pending withdrawal for admin execution.

---

## Admin-side extras to spot-check

- **Worklist KPIs** update as you go: KYC review, MT5 setup, Awaiting activation, Compensation
  review, **Due for payout**, Withdrawals pending.
- **Due for payout** section lists the approved claim (scheduled 27th). **Run payout job** triggers
  the monthly job manually; it pays only claims whose scheduled date has arrived (so nothing pays
  before the 27th — expected).
- **Account detail** (`/admin/accounts/{id}`) shows the trader identity, balance composition, open
  items, and the same unified **Recent activity** feed.

---

## Additional flow tests (state-machine branches)

These cover the branches the happy path doesn't. Some need a little setup via SQL (psql) because
the Stub has no trading data and payouts are date-gated. All verified passing.

> psql helper: `PGPASSWORD=root psql -h localhost -U postgres -d mondshield -c "<SQL>"`

### A. Compensation **reject** (no stage change)
- Active trader submits a claim → admin **Start review** → **Reject request**.
- ✅ **Expected:** request status **Rejected**; account **stays** at its stage (no drop). Rejection
  only happens from `UnderReview` (Submitted must be started first).

### B. Compensation **payout + stage drop** (the core state machine)
- Approve a claim (it schedules for the 27th). Force it due:
  `UPDATE compensation_requests SET scheduled_payout_date_utc = now() - interval '1 day' WHERE id='<id>';`
- Admin → **Run payout job** (or wait for the 27th).
- ✅ **Expected:** `{"paidCount":1}`, then:
  - claim status **Paid**;
  - account drops **Stage 1 → Rebuild** (a Stage1/2/3/VIP claim always drops to Rebuild);
  - balance composition gains the payout as **Compensation** (un-insured) — e.g. $500 → total rises;
  - lifetime cap usage increases (e.g. $500 of $5,000, $4,500 remaining);
  - a `stage_transitions` row: `Stage1 → Rebuild, Down`;
  - **dashboard** now shows Rebuild rates (**30% / 20%**) with "YOU" on Rebuild, and **History**
    lists Deposit + Compensation + StageDown.

### C. **$5,000 lifetime cap**
- A trader submits a claim whose computed coverage exceeds the remaining cap — e.g. loss $12,000 at
  Stage 1 (50%) = $6,000 computed.
- ✅ **Expected:** `computedCoverage=6000`, **`cappedAmount=5000`**, **`capReached=true`** — the
  payout is clamped to the lifetime cap.

### D. **Withdrawal** request + admin completion
- Give the account profit (the Stub has none): `UPDATE shield_accounts SET composition_profit=1000 WHERE id='<id>';`
- Trader requests a $500 withdrawal.
- ✅ **Expected:** `profitPortion=500`, **`brokerShare=150`** (Stage 1 = 30%), **`net=350`**, status
  **Requested**; it appears in the admin **pending withdrawals** queue.
- Admin → **Mark completed**.
- ✅ **Expected:** status **Completed**.

### E. **Full stage ladder** — every transition (up and down)

Stage **up** is an admin action (`POST /api/admin/accounts/{id}/level-up`, or the **Level up**
button on the admin account-detail page) — the admin confirms the trader met the profit target,
since the app doesn't poll MT5 trades. Stage **down** happens automatically on a compensation
payout. Verified every edge:

| Direction | Transition | How | ✅ |
|---|---|---|---|
| UP | Stage1 → Stage2 → Stage3 → VIP | Level up ×3 (rates 55/25, 60/20, 30/0) | ✅ |
| UP | Rebuild → Stage1 | Level up | ✅ |
| UP | Revival → Rebuild | Level up | ✅ |
| UP | VIP → (none) | Level up rejected — *"Vip is the top stage"* (400) | ✅ |
| DOWN | Stage1 / Stage2 / Stage3 / VIP → Rebuild | compensation payout | ✅ |
| DOWN | Rebuild → Revival | compensation payout | ✅ |
| DOWN | Revival → **Exit** (status `Exited`) | compensation payout | ✅ |

Note: **one compensation request per stage** per account (lifetime) — a second claim at the same
stage is rejected. To cycle a trader through many drops, use separate traders (as the ladder test
does).

### F. Admin panel walkthrough (as the admin user, in the UI)
- Sign in as admin → **Worklist**: onboarding queue lists pending traders by **name + email**;
  KYC/MT5/activation/compensation/payout/withdrawal KPIs.
- **Approve KYC** button → row advances to *KycApproved* and shows **Provision MT5** (live update).
- Provision → Activate inline forms complete onboarding.
- **Compensation review** page → identity, coverage calc, cap; **Start review** → **Approve/Reject**.
- **Account detail** → **Stage control** card with **Level up → <next stage>** (disabled at VIP);
  balance composition; open items; unified recent-activity feed.
- **Run payout job** processes due compensations.

## Notes

- **Responsive:** resize to mobile — the sidebar collapses to a bottom nav and tables become
  stacked cards.
- **Auth:** access token is in memory, refresh token in localStorage; a 401 auto-refreshes once.
  Logging the same account in elsewhere (e.g. a curl script) rotates the refresh token and will
  log the browser tab out on its next refresh — a single-session backend constraint, handled
  gracefully.
