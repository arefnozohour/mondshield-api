# MT5 Manager API integration

The backend talks to MetaTrader 5 through the **Manager API** behind the `IMt5Client` port
(`MondShield.Application.Mt5`). There are two implementations, selected by config:

| `Mt5:Mode` | Implementation | Needs a server? |
|------------|----------------|-----------------|
| `Stub` (default) | `Mt5StubClient` — in-memory fake | No |
| `Live` | `Mt5ManagerClient` — real MetaQuotes Manager API | Yes |

Everything upstream (onboarding provisioning, the monthly payout job, balance/trade reads)
depends only on `IMt5Client`, so flipping modes changes nothing else.

## What's vendored

The MetaQuotes SDK isn't on NuGet, so the DLLs live in `libs/mt5/` (git-tracked):

- **Managed wrappers** (referenced for compile + runtime, listed in `deps.json`):
  `MetaQuotes.MT5ManagerAPI64.dll`, `MetaQuotes.MT5CommonAPI64.dll`
- **Native** (copied next to the exe; the CPU-appropriate variant is auto-selected at runtime):
  `MT5APIManager64.dll`, `…avx.dll`, `…avx2.dll`, `…arm.dll`

These are 64-bit, so the API builds/runs as **x64** (`<PlatformTarget>x64</PlatformTarget>` +
`win-x64` in `MondShield.Api.csproj`). Verified loadable and initializable under **.NET 10** —
no .NET Framework bridge/sidecar is required.

> To refresh the DLLs from a newer SDK: run the MetaTrader5 SDK extractor, then copy the six
> files above from its `Libs/` folder into `libs/mt5/`, overwriting.

## Going live

1. **Supply the connection secrets** (never commit these — same rule as the DB connection string
   and JWT key). From `src/MondShield.Api`:

   ```bash
   dotnet user-secrets set "Mt5:Server"          "YOUR_IP:PORT"        # e.g. 203.0.113.10:443
   dotnet user-secrets set "Mt5:ManagerLogin"    "YOUR_MANAGER_LOGIN"
   dotnet user-secrets set "Mt5:ManagerPassword" "YOUR_PASSWORD"
   ```

   The manager account must have **Manager API access** on the server, and the server may need to
   **whitelist this machine's IP**.

2. **Switch to live mode** — set `Mt5:Mode=Live` (via user-secrets, env `Mt5__Mode=Live`, or
   `appsettings.{Environment}.json`).

3. **Confirm the provisioning group exists.** New accounts are created into `Mt5:DefaultGroup`
   (default `demo\mondshield`) with `Mt5:DefaultLeverage` (default `100`). The group must exist
   on your server or `UserAdd` fails.

4. **Start the API.** The connection is established lazily on first MT5 use and reused
   (singleton). A wrong address/credentials surfaces as a clear error, e.g.
   `MT5 Connect to '…' as manager … failed: MT_RET_ERR_NETWORK`.

## Operation mapping (`Mt5ManagerClient`)

| `IMt5Client` method | MT5 Manager API call |
|---------------------|----------------------|
| `CreateAccountAsync` | `UserCreate` → set group/name/email/leverage/rights → `UserAdd(user, mainPwd, investorPwd)`; server auto-assigns the login |
| `GetAccountSnapshotAsync` | `UserAccountRequest` → `Balance()` / `Equity()` / `Credit()` |
| `GetTradeHistoryAsync` | `DealRequest(login, from, to)` → keep only `DEAL_BUY`/`DEAL_SELL`, map `Profit()` + `Commission()` |
| `GetBalanceOperationsAsync` | `DealRequest(login, from, to)` → keep only `DEAL_BALANCE`, map `Deal()` (ticket) + `Profit()` (signed amount) + `Comment()` |
| `CreditBalanceAsync` | `DealerBalance(login, amount, DEAL_BALANCE, comment)` (compensation payout) |

## Tracking balance operations (deposits/withdrawals made directly on MT5)

MT5 shows only one balance number, and money can enter or leave a login entirely outside our flows —
a trader top-up, a manual dealer credit, or a withdrawal done in the Manager terminal. These are
never trades; in MT5 they are `DEAL_BALANCE` deals. Reconciliation captures them so they stop
surfacing only as anonymous "drift":

1. **Capture.** `Mt5ReconciliationService` reads `GetBalanceOperationsAsync` over the same window as
   trade history and records each new `DEAL_BALANCE` deal as an `Mt5BalanceOperation`, keyed by its
   MT5 **deal ticket** (unique index on `(mt5_login, deal_id)`) so a deposit is never recorded twice.
2. **Auto-attribution.** A deal whose comment starts with the MondShield marker (see `Mt5Comments`)
   is one **we** originated — e.g. a compensation payout, already booked in the ledger by its own
   flow — so it is recorded as `RecordedFromSystem` and NOT booked again. Everything else is
   **external**, recorded as `PendingReview`.
3. **Classification (admin).** Pending external ops appear at
   `GET /api/admin/mt5/balance-operations/pending`. An admin classifies each incoming deposit into a
   composition bucket (`POST .../{id}/classify` with `InsuredCapital` | `Compensation` | `Profit`),
   which books the matching ledger entry and closes the drift, or `POST .../{id}/ignore` for a
   withdrawal / non-coverage movement (handled by the profit-withdrawal flow instead).

> **Why a human classifies:** the rules bucket incoming money differently — only a qualifying
> $2,000 deposit is *insured capital*; broker-paid money is *compensation* (no coverage); the rest is
> plain tradable funds. There is no reliable signal on the MT5 side to tell these apart, so an
> external deposit is queued rather than guessed. The initial activation deposit is a common case:
> it is pre-booked at onboarding, so when the real MT5 deposit lands the admin should **Ignore** the
> pending op (it is already in the ledger) rather than classify it a second time.

> ⚠️ **Schema note.** `mt5_balance_operations` is a new table. With
> `Database:RecreateOnStartup=false` (the default outside a fresh dev DB), the startup schema build
> is skipped once any table exists, so this table will be **missing** until the schema is rebuilt.
> After pulling this change, either run once with `RecreateOnStartup=true` (drops & rebuilds — wipes
> dev data) or drop the `public` schema so it regenerates. Before production this becomes a normal
> migration.

## Real-time tracking (pump/sink)

By default the connection is request/response only (`PUMP_MODE_NONE`) and the hourly job is what
reconciles. Setting `Mt5:Realtime:Enabled=true` (Live mode only) switches the connection to pump
mode (`PUMP_MODE_USERS | PUMP_MODE_ACTIVITY`) and wires two sinks:

- **`CIMTDealSink.OnDealAdd`** — fires on every new deal (trade *and* balance operation). The
  callback copies out only the login and hands it to `Mt5RealtimeListener`.
- **`CIMTManagerSink.OnDisconnect`** — flags a dropped connection so the listener's heartbeat
  reconnects.

`Mt5RealtimeListener` (a hosted service) debounces a burst of deals per account (`DebounceMs`) and
then calls the **same** `IMt5ReconciliationService.ReconcileAccountAsync` the hourly job uses — so
real-time is purely a latency win (seconds instead of up to an hour) and cannot double-book: the
reconciler is idempotent (trade watermark + the `(mt5_login, deal_id)` unique index). The hourly job
stays on as a backstop. One shared Manager connection is reused for both request/response and the
pump, because a manager login typically allows only one server session.

> ⚠️ **Enable only after the native connect is verified on the host.** The listener's heartbeat
> auto-connects every `HeartbeatSeconds`. If the native `Connect` is unstable on a given host, that
> turns an occasional failure into a crash-loop. Confirm `GET /api/admin/mt5/status` reports
> `Connected` first, then set `Enabled=true`.

> **Known issue (unresolved):** on the dev machine used so far, the native Manager `Connect` crashes
> the **process** (hard exit, no managed exception) — reproducible even with the pump OFF, i.e. in
> the plain request/response path, so it is a native-integration/environment problem, not the
> real-time code. At the time it was seen: the six native/managed DLLs were present next to the exe
> (the `win-x64` output), config loaded, and the broker was reachable (TCP 443 open). Likely
> suspects to check on the real host: manager account API access / IP whitelist, and the native API
> build vs. server version. Until `GET /api/admin/mt5/status` returns `Connected` on the host, none
> of the Live paths (provisioning, payout, reconciliation, real-time) can run.

## Operational notes

- **Thread safety.** The native Manager API isn't safe for concurrent calls, so `Mt5ManagerClient`
  serializes every operation behind one lock. Call volume is low (provisioning + the monthly
  payout), so this is ample.
- **Pump mode.** Connects with `PUMP_MODE_USERS` (streams only user data — enough for the
  request-based user/balance/deal operations, without the memory cost of `PUMP_MODE_FULL` on a
  broker-scale server). Bump it in `Mt5ManagerClient.EnsureConnected` if an operation needs more.
- **Passwords.** Generated per account (main + investor) and returned once from
  `CreateAccountAsync` — the trader never picks an MT5 login/password.
