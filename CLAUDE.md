# CLAUDE.md — MondShield Account System

Project guidance for Claude Code. Read this before writing or editing code.

> ⚠️ SPEC SOURCE: The authoritative spec is the Farsi PDF + diagram from the broker.
> The public webpage (mondfx.com/mondshield) is SIMPLIFIED and WRONG in several places
> (it says 3 stages / $1,000 / flat 60%). Trust the rules below, not the webpage.

## What this project is

A full-stack system that manages the **MondShield account type** for a forex broker — an
insurance-style layer on top of an MT5 trading account. It covers a tiered % of qualifying
trading losses, runs a bidirectional multi-stage state machine, takes a broker profit-share
on profit withdrawals, and processes compensation requests on a fixed monthly schedule.

**Scope of this build (intentionally limited):** the MondShield account type, its stages,
the compensation ticket flow, and the broker profit-share calculation. We are **NOT** building
trading, order execution, buy/sell, or charting. Those live in MT5 itself.

## Stack decisions (do not deviate without asking)

- **Backend:** .NET 10 (current LTS), ASP.NET Core Web API (full C#/.NET — single backend, no separate gateway)
- **ORM:** Entity Framework Core with the Npgsql provider
- **Database:** PostgreSQL
- **Auth:** lightweight — a single `users` table (email + PBKDF2-hashed password + one `role`
  column), JWT bearer access tokens + rotating refresh tokens. Two roles only (`User`, `Admin`).
  Deliberately NOT full ASP.NET Core Identity: no MFA, lockout, email confirmation, external
  logins, claims/roles join tables, or security/concurrency stamps. Password hashing uses the
  framework's `PasswordHasher<T>` (from the ASP.NET shared framework — no Identity EF stores).
- **Background jobs:** Hangfire (persisted jobs + dashboard — used for the monthly payout job)
- **Frontend:** React + TypeScript (two panels: User and Admin — role-gated)
- **MT5 integration:** `MetaQuotes.MT5ManagerAPI.dll` referenced directly in the Infrastructure
  project. Native .NET assembly — **this is why the whole backend is C# and must run on Windows.**

### Hosting constraint
The backend **must run on Windows** (Windows Server or Windows container) because the MT5
Manager API DLL is Windows-native. Do not assume Linux/Docker-on-Linux hosting for the backend.

### Build & tooling
- **SDK:** pinned to the .NET 10 line via `global.json` (`rollForward: latestMinor`). Build with
  the 10.x SDK — an 8.x SDK cannot target `net10.0`.
- **Package versions:** Central Package Management — every version lives in
  `Directory.Packages.props`; `<PackageReference>` items in csproj are version-less. Shared MSBuild
  settings (TargetFramework, Nullable, ImplicitUsings) live in `Directory.Build.props`. Do not put a
  `Version=` on a PackageReference, and do not hardcode `TargetFramework` in individual csproj files.
- **Schema (early dev — NO migrations yet):** the model is the source of truth. On startup in
  Development the API calls `EnsureCreated()` to build the schema directly from the entities, then
  seeds the bootstrap admin. With `Database:RecreateOnStartup=true` (default in
  `appsettings.Development.json`) it drops & rebuilds every run, so entity changes are picked up
  automatically — do NOT write a migration for each change during this phase. `EnsureCreated()`
  alone does not alter an existing schema, which is why recreate-on-startup is on.
  Set `RecreateOnStartup=false` to keep data between runs (then drop the DB manually after a model change).
- **Migrations (later, before production):** `dotnet-ef` is already a local tool
  (`.config/dotnet-tools.json`). When ready, switch startup to `MigrateAsync()`, generate the first
  migration into `MondShield.Infrastructure/Persistence/Migrations` (API is the startup project):
  `dotnet dotnet-ef migrations add <Name> --project src/MondShield.Infrastructure --startup-project src/MondShield.Api --output-dir Persistence/Migrations`.
  `EnsureCreated()` and migrations are mutually exclusive — don't mix them on the same database.
- **Config/secrets:** the Postgres connection string (`ConnectionStrings:Default`) and `Jwt:SigningKey`
  must be supplied per-environment (user-secrets / env), never committed with real values.
- **Tests:** none — do not generate test projects or test code unless explicitly asked.

## Solution structure (Clean Architecture)

```
MondShield.sln
├── MondShield.Api             controllers, auth, DI composition root
├── MondShield.Domain          entities, stage machine, rules, money math — NO dependencies
├── MondShield.Application      use cases, ticket flow, payout orchestration
└── MondShield.Infrastructure   EF Core, Hangfire, MT5 wrapper
```

**Hard rule:** `MondShield.Domain` stays dependency-free. The stage machine, eligibility rules,
loss-coverage math, and profit-share math must be pure and deterministic, with
no database or MT5 calls.

## Stage model (THE CORE — 6 levels, bidirectional)

The diagram reads right-to-left (Farsi). Two directions of movement:
- **UP** = earn the profit target within 30 calendar days of first trade → advance one stage.
- **DOWN** = file a compensation (loss) request → receive that stage's payout, then drop.

| Stage              | Coverage % | Broker share % | Level-up requirement      |
|--------------------|-----------:|---------------:|---------------------------|
| Revival (احیا)     |        20% |            10% | 15% profit in 30 days     |
| Rebuild (بازسازی)  |        30% |            20% | 15% profit in 30 days     |
| Stage 1 (مرحله اول)|        50% |            30% | 10% profit in 30 days     |
| Stage 2 (مرحله دوم)|        55% |            25% | 10% profit in 30 days     |
| Stage 3 (مرحله سوم)|        60% |            20% | 10% profit in 30 days     |
| VIP / Golden Peak (قله طلایی) | 30% | 0%       | top — no level-up         |

### Up transitions (by profit)
Revival → Rebuild → Stage 1 → Stage 2 → Stage 3 → VIP.
Requirement: profit target met within 30 calendar days of first trade.
- Stages 1, 2, 3: need **10%** profit.
- Revival, Rebuild: need **15%** profit.

### Down transitions (by compensation request)
- A compensation request from Stage 1, 2, 3, or VIP → pays that stage's coverage, then drops
  **directly to Rebuild**.
- A compensation request from Rebuild → drops to **Revival**.
- A compensation request from Revival → trader **EXITS the program completely** after payout.
  (Revival is the last chance to stay in.)

> ❓ OPEN QUESTION: confirm whether dropping from Stage 2/3/VIP goes straight to Rebuild
> (skipping intermediate stages). Docs say "directly to Rebuild" — model it that way but
> keep the transition resolved by a single function so it's easy to correct.

## Money rules

### Minimum deposit & activation
- Starting the program (Stage 1) requires a deposit of **$2,000** (NOT $1,000).
- Coverage is active from the first trade — no waiting period.

### Loss compensation
- Payout = stage coverage % × total trading loss, **excluding commission paid by the trader**.
  (Stage 1 = 50% of loss, etc. per the table above.)
- **Lifetime cap: max $5,000 compensation per person.**
  > ❓ OPEN QUESTION: confirm $5,000 is lifetime vs. per-request. Modelled as lifetime for now.
- Compensation is paid into the SAME MondShield account, BUT that money does NOT carry
  insurance coverage in the new stage.
- Compensation money is tradable and withdrawable.

### Re-deposit rule (critical — easy to get wrong)
After receiving compensation and dropping a stage, the compensation money does NOT count
toward activating the new stage. To start the lower stage you must deposit **$2,000 again**,
separate from the compensation money, because broker-paid compensation carries no coverage.
- Worked example from spec: receive $500 compensation in Stage 1, drop to Rebuild. Depositing
  $1,500 to make $2,000 total (your money + compensation) is NOT valid. You need a fresh $2,000
  on top of the compensation.

### Broker profit-share (subsystem — applies on PROFIT withdrawals)
- On withdrawal of profit, the broker takes the stage's share % — but ONLY on the profit
  portion, NEVER on original deposited capital or on compensation money.
- Worked example: deposit $2,000, profit +$1,000, withdraw $1,000 → broker share (Stage 1, 30%)
  = $300 → trader receives $700, original $2,000 deposit remains intact and insured.
- Mixed example: total profit earned $10, trader withdraws $20 → broker shares only in the $10
  of profit; the other $10 (from original capital) is not shared.
- Commission excluded from coverage by design: commissions + broker profit-share are the
  funding source for loss compensation.

### Capital-protection rule
As long as the trader withdraws only profit (without the withdrawal harming the initial
deposit) and shares profit with the broker, the original capital (initial deposit) stays
under insurance coverage.

### VIP (Golden Peak)
- Normally VIP requires a $10,000 deposit, but reaching Stage 4 (Golden Peak) grants VIP
  status automatically. Coverage 30%, broker share 0%.

## Balance composition (the hardest modelling problem)

The MT5 account shows ONE balance number. Our system must track the COMPOSITION of that
balance in our own ledger, because the rules treat the parts differently:
- **Insured capital** — the trader's qualifying $2,000 deposit(s). Under coverage. Profit-share
  on profit derived from it.
- **Compensation money** — broker-paid. Tradable/withdrawable but NOT insured, NOT counted
  toward stage activation.
- **Profit** — subject to broker profit-share on withdrawal.
- **Commission** — excluded from all coverage and share math.

Every deposit, payout, profit, and withdrawal must update this composition in the local ledger.
The local ledger is the source of truth on our side; MT5 is reconciled against it.

## Review & deposit dates
- A compensation request can be submitted any time during the month, but only after the
  account reaches the qualifying loss condition. One request per stage.
- After submission the account is reviewed in full.
- Compensation is deposited EXCLUSIVELY on the 27th or 28th of the Gregorian month — no
  exceptions. (Hangfire scheduled job.)

## Money flow (hybrid)
- **Payouts (automated):** the monthly job picks up APPROVED compensation requests, computes
  the capped amount, records it in the ledger FIRST, then credits the MT5 account via the
  Manager API, then applies the down-stage transition.
- **Withdrawals (manual):** create a flagged record + admin notification. A human executes the
  withdrawal in the MT5 Manager terminal (after the profit-share calc is shown), then marks it
  done. No withdrawal automation in this build — but the profit-share AMOUNT is computed by us.

## Core domain model

- **ShieldAccount** — MT5 login, currentStage, firstTradeAt, status, balance-composition refs
- **Stage** — level enum + config (coveragePercent, brokerSharePercent, levelUpProfitTarget,
  levelUpDays). Drive these from the table above; do not scatter magic numbers.
- **CompensationRequest** — accountId, stageAtRequest, lossAtRequest, commissionExcluded,
  computedCoverage, cappedAmount, status (SUBMITTED → UNDER_REVIEW → APPROVED/REJECTED → PAID),
  scheduledPayoutDate
- **StageTransition** — immutable audit log (from, to, direction, reason, timestamp)
- **LedgerEntry** — every credit/debit + which composition bucket it affects
- **ProfitWithdrawal** — requested amount, profit portion, broker share, net to trader
- **CompensationCapTracker** — per-person lifetime total against the $5,000 cap
- **MT5 wrapper interface** — minimal surface: read balance/equity, read trade history &
  commission, credit balance. Behind an interface in Application so it can be stubbed.

## Build order
1. Domain: stage config table, stage machine (up + down), loss-coverage math, profit-share
   math, cap logic, balance-composition logic — pure and deterministic, matching the worked
   examples in this file.
2. Persistence + EF Core migrations (schema, audit + ledger + cap-tracker tables).
3. MT5 wrapper — stub first (fake balances + commission), integrate without a live server.
4. Compensation ticket flow end-to-end against the stub.
5. Scheduler — qualifying-loss detection + 27th/28th Hangfire payout job + down-transition.
6. Profit-withdrawal flow — compute share, create manual-withdrawal worklist item.
7. Admin panel — review queue, approve/reject, payout trigger, manual-withdrawal worklist,
   cap monitoring.
8. User panel — account status, stage, balance composition, submit request, history.
9. Swap MT5 stub for the real DLL; validate against a demo MT5 server.

## Repository layout
- This is the **backend repo** (`mondshield-api`). The frontend lives in a SEPARATE repo
  (`mondshield-web`), chosen because the backend is Windows-only (MT5 DLL) while the frontend
  is platform-agnostic — different toolchains, CI runners, and deploy targets.
- **API contract:** the ASP.NET Core OpenAPI/Swagger spec is the PUBLISHED CONTRACT between the
  two repos. Whenever an endpoint, request, or response shape changes, regenerate and COMMIT the
  OpenAPI spec — the frontend generates its typed API client from it, so an out-of-date spec
  silently breaks the frontend. Treat the committed spec as part of the change, not an afterthought.

## Conventions
- **DB naming:** all tables and columns are `snake_case`, applied globally via
  `UseSnakeCaseNamingConvention()` (EFCore.NamingConventions) on the DbContext. Do NOT hand-name
  tables/columns in PascalCase — define entities/properties normally and let the convention map
  them (e.g. `RefreshTokenExpiresAtUtc` → `refresh_token_expires_at_utc`). New domain tables follow
  this automatically; only override a table name to drop an unwanted prefix.
- **Auth schema is intentionally minimal:** one `users` table only (no `roles`/`user_roles`/
  claims/tokens tables — ASP.NET Core Identity is not used). Role is a single string column on
  `users`. Don't reintroduce Identity tables when adding migrations.
- Money is `decimal`, never `double`/`float`.
- All stage transitions, ledger writes, and cap updates are auditable and append-only.
- Stage parameters come from one config source (the table), never hardcoded inline.
- Before wiring the real MT5 DLL, check the SDK's bundled C# sample projects (login, account
  read, balance ops, commission/history) to confirm the actual API surface.

## Open questions to confirm with the broker
- Is the $5,000 compensation cap lifetime or per-request? (modelled as lifetime)
- Does a down-transition from Stage 2/3/VIP go straight to Rebuild, skipping stages? (modelled yes)
- Exact definition of "total trading loss" for coverage (realized vs. drawdown vs. net).
- How profit vs. capital is determined at withdrawal time when balances are mixed.

## Notes / context
- The MT5 Manager SDK ships its own docs (a `.chm` help file or `Help` folder beside the EXE)
  and C# sample projects — primary reference for the Manager API (not fully publicly documented).
- The public MondShield page lists restricted regions (incl. Turkey) where the broker does not
  offer the service. Affects operating the broker, not building the software, but worth noting.
- Source spec documents are in Farsi; this file is the English working translation.
