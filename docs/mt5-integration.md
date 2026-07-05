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
| `CreditBalanceAsync` | `DealerBalance(login, amount, DEAL_BALANCE, comment)` (compensation payout) |

## Operational notes

- **Thread safety.** The native Manager API isn't safe for concurrent calls, so `Mt5ManagerClient`
  serializes every operation behind one lock. Call volume is low (provisioning + the monthly
  payout), so this is ample.
- **Pump mode.** Connects with `PUMP_MODE_USERS` (streams only user data — enough for the
  request-based user/balance/deal operations, without the memory cost of `PUMP_MODE_FULL` on a
  broker-scale server). Bump it in `Mt5ManagerClient.EnsureConnected` if an operation needs more.
- **Passwords.** Generated per account (main + investor) and returned once from
  `CreateAccountAsync` — the trader never picks an MT5 login/password.
