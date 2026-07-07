#!/usr/bin/env pwsh
<#
.SYNOPSIS
    MondShield full end-to-end API scenario — drives the whole trader lifecycle against a running
    backend and asserts each response. Repeatable, no clicking. This is an ops/QA driver script,
    NOT an xUnit test project.

.DESCRIPTION
    Exercises every major flow through the HTTP API (plus a few psql pokes for the bits the Stub
    can't produce — date-gated payouts, injected profit, and the crash-simulated "Paying" state):

      1.  Onboarding via self-register  (create account + MT5 provision + $2,000 activation)
      2.  Manual admin onboarding       (KYC -> provision -> activate, against a seeded PendingKyc user)
      3.  Compensation happy path       (submit -> review -> approve -> force-due -> payout -> stage drop)
      4.  Reconciliation: CONFIRM       (stuck 'Paying' -> confirm-paid -> Paid, single credit)
      5.  Reconciliation: RESET         (stuck 'Paying' -> reset-approved -> re-paid by the job)
      6.  Reconciliation guard          (confirm-paid on a non-Paying request is rejected)
      7.  $5,000 lifetime cap clamp
      8.  Profit withdrawal + admin completion
      9.  Full stage ladder up          (Stage1 -> Stage2 -> Stage3 -> VIP -> rejected at top)
      10. Compensation reject           (no stage change)

    Every step prints a green check or red cross; a summary with the pass/fail tally is printed at
    the end and the script exits non-zero if anything failed.

.PARAMETER ApiBase
    Backend base URL. Default http://localhost:5259 (the 'http' launch profile).

.NOTES
    Prerequisites:
      * Backend running (dotnet run --project src/MondShield.Api --launch-profile http).
      * Mt5:Mode = Stub  (so provisioning/credit succeed without a live MT5 server).
      * A fresh-ish DB helps step 2 (seeded user1 must still be PendingKyc); step 2 self-skips
        if it isn't. Set Database:RecreateOnStartup=true for a clean, fully-green run.
      * psql on PATH (used for date-gating, profit injection, and the simulated 'Paying' crash).

.EXAMPLE
    pwsh ./scripts/e2e-scenario.ps1
#>

[CmdletBinding()]
param(
    [string]$ApiBase = "http://localhost:5259",
    [string]$AdminEmail = "admin@mondshield.local",
    [string]$AdminPassword = "Admin#12345",
    [string]$SeedUserEmail = "user1@mondshield.local",
    [string]$SeedUserPassword = "User#12345",
    [string]$PgHost = "localhost",
    [int]$PgPort = 5432,
    [string]$PgDb = "mondshield",
    [string]$PgUser = "postgres",
    [string]$PgPassword = "root"
)

$ErrorActionPreference = "Stop"
$ApiBase = $ApiBase.TrimEnd('/')
$stamp = Get-Date -Format 'yyyyMMddHHmmss'
$script:pass = 0
$script:fail = 0

# ---------------------------------------------------------------------------- helpers

function Section($title) {
    Write-Host ""
    Write-Host "== $title " -ForegroundColor Cyan -NoNewline
    Write-Host ("=" * [Math]::Max(2, 74 - $title.Length)) -ForegroundColor DarkCyan
}

function Check($desc, [bool]$cond, $detail = $null) {
    if ($cond) {
        $script:pass++
        Write-Host "  [PASS] $desc" -ForegroundColor Green
    }
    else {
        $script:fail++
        Write-Host "  [FAIL] $desc" -ForegroundColor Red
        if ($null -ne $detail) { Write-Host "         $detail" -ForegroundColor DarkGray }
    }
}

# HTTP call that never throws on 4xx/5xx — returns { Status, Body }.
function Api {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [string]$Token,
        $Body
    )
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $splat = @{
        Method             = $Method
        Uri                = "$ApiBase$Path"
        Headers            = $headers
        SkipHttpErrorCheck = $true
        StatusCodeVariable = 'sc'
    }
    if ($null -ne $Body) {
        $splat['Body'] = ($Body | ConvertTo-Json -Depth 8 -Compress)
        $splat['ContentType'] = 'application/json'
    }
    $resp = Invoke-RestMethod @splat
    return [pscustomobject]@{ Status = $sc; Body = $resp }
}

# psql one-liner — returns trimmed scalar output.
function Sql($query) {
    $env:PGPASSWORD = $PgPassword
    $out = & psql -h $PgHost -p $PgPort -U $PgUser -d $PgDb -t -A -c $query 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed for [$query]: $out" }
    return ($out | Out-String).Trim()
}

function Login($email, $password) {
    $r = Api POST '/api/Auth/login' $null @{ email = $email; password = $password }
    if ($r.Status -ne 200) { throw "login failed for $email : HTTP $($r.Status)" }
    return $r.Body.accessToken
}

# Self-register a trader; returns tokens + accountId + userId. Register auto-provisions MT5 and
# activates at Stage 1 with $2,000, so the returned trader is already Active.
function New-Trader($label) {
    $email = "${label}_${stamp}@test.local"
    $r = Api POST '/api/Auth/register' $null @{ email = $email; password = 'Scenario#123'; fullName = "Trader $label" }
    if ($r.Status -ne 200) { throw "register $label failed: HTTP $($r.Status) $($r.Body | ConvertTo-Json -Compress)" }
    $token = $r.Body.accessToken
    $me = Api GET '/api/accounts/me' $token
    if ($me.Status -ne 200) { throw "GET /accounts/me for $label failed: HTTP $($me.Status)" }
    return [pscustomobject]@{
        Label     = $label
        Email     = $email
        UserId    = $r.Body.userId
        Token     = $token
        AccountId = $me.Body.accountId
        Account   = $me.Body
    }
}

function Get-Account($token) { (Api GET '/api/accounts/me' $token).Body }
function Get-AdminRequest($adminToken, $id) { (Api GET "/api/admin/compensation-requests/$id" $adminToken).Body }

# Submit + admin-approve a compensation claim; returns the request id (status Approved).
function Approve-Claim($trader, $adminToken, $loss, $commission) {
    $sub = Api POST '/api/compensation-requests' $trader.Token @{ totalTradingLoss = $loss; commissionPaid = $commission }
    if ($sub.Status -ne 200) { throw "submit claim ($($trader.Label)) failed: HTTP $($sub.Status) $($sub.Body | ConvertTo-Json -Compress)" }
    $id = $sub.Body.id
    $sr = Api POST "/api/admin/compensation-requests/$id/start-review" $adminToken
    if ($sr.Status -ne 204) { throw "start-review failed: HTTP $($sr.Status)" }
    $ap = Api POST "/api/admin/compensation-requests/$id/approve" $adminToken @{ reviewerNote = "e2e $stamp" }
    if ($ap.Status -ne 204) { throw "approve failed: HTTP $($ap.Status)" }
    return [pscustomobject]@{ Id = $id; Submit = $sub.Body }
}

# ---------------------------------------------------------------------------- preflight

Section "Preflight"
try {
    $ping = Invoke-RestMethod -Method GET -Uri "$ApiBase/swagger/v1/swagger.json" -SkipHttpErrorCheck -StatusCodeVariable pc
    Check "Backend reachable at $ApiBase (HTTP $pc)" ($pc -eq 200) "Start it: dotnet run --project src/MondShield.Api --launch-profile http"
    if ($pc -ne 200) { Write-Host "Aborting — backend not reachable." -ForegroundColor Red; exit 1 }
}
catch {
    Write-Host "Aborting — backend not reachable at $ApiBase ($($_.Exception.Message))." -ForegroundColor Red
    exit 1
}
if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    Write-Host "Aborting — psql not on PATH (needed for date-gating / profit / stuck-state SQL)." -ForegroundColor Red
    exit 1
}
Check "psql available" $true
try { $mode = Sql "SELECT 1"; Check "Database reachable ($PgDb)" ($mode -eq '1') } catch { Check "Database reachable" $false $_.Exception.Message; exit 1 }

$admin = Login $AdminEmail $AdminPassword
Check "Admin login" ([bool]$admin)

# ---------------------------------------------------------------- 1. onboarding via register

Section "1. Onboarding via self-register (account + MT5 + $2,000 activation)"
$t1 = New-Trader 't1'
$a1 = $t1.Account
Check "Account created and Active"            ($a1.status -eq 'Active')       "status=$($a1.status)"
Check "Started at Stage 1"                    ($a1.currentStage -eq 'Stage1') "stage=$($a1.currentStage)"
Check "MT5 account provisioned (login set)"   ($null -ne $a1.mt5Login)        "mt5Login=$($a1.mt5Login)"
Check "Insured capital = 2000"               ([decimal]$a1.composition.insuredCapital -eq 2000) "insured=$($a1.composition.insuredCapital)"
Check "Compensation/Profit/Commission = 0"   (([decimal]$a1.composition.compensation -eq 0) -and ([decimal]$a1.composition.profit -eq 0) -and ([decimal]$a1.composition.commission -eq 0))
Check "Total balance = 2000"                 ([decimal]$a1.composition.total -eq 2000) "total=$($a1.composition.total)"

# ------------------------------------------------- 2. manual admin onboarding (KYC->provision->activate)

Section "2. Manual admin onboarding (KYC -> provision -> activate)"
$seedTok = Login $SeedUserEmail $SeedUserPassword
$seed = Get-Account $seedTok
if ($seed.status -eq 'PendingKyc') {
    $acc = $seed.accountId
    $k = Api POST "/api/admin/accounts/$acc/approve-kyc" $admin
    Check "Approve KYC (PendingKyc -> KycApproved)" ($k.Status -eq 204) "HTTP $($k.Status)"
    $p = Api POST "/api/admin/accounts/$acc/provision-mt5" $admin @{ fullName = "Seed User One"; email = $SeedUserEmail }
    Check "Provision MT5 (returns login)" ($p.Status -eq 200 -and $null -ne $p.Body.mt5Login) "HTTP $($p.Status) login=$($p.Body.mt5Login)"
    $act = Api POST "/api/admin/accounts/$acc/activate" $admin @{ depositAmount = 2000 }
    Check "Activate with `$2000 (Provisioned -> Active)" ($act.Status -eq 204) "HTTP $($act.Status)"
    $seed2 = Get-Account $seedTok
    Check "Seeded account now Active @ Stage1 w/ 2000" (
        $seed2.status -eq 'Active' -and $seed2.currentStage -eq 'Stage1' -and [decimal]$seed2.composition.insuredCapital -eq 2000)
}
else {
    Write-Host "  [SKIP] seeded user is '$($seed.status)', not PendingKyc — re-run with a fresh DB to cover this path." -ForegroundColor Yellow
}

# --------------------------------------------- 3. compensation happy path + payout + stage drop

Section "3. Compensation: submit -> approve -> payout -> stage drop (trader t1)"
$sub = Api POST '/api/compensation-requests' $t1.Token @{ totalTradingLoss = 600; commissionPaid = 40 }
Check "Submit claim (loss 600, commission 40)" ($sub.Status -eq 200) "HTTP $($sub.Status)"
$req1 = $sub.Body
Check "Computed coverage = 280 (50% of 560)"  ([decimal]$req1.computedCoverage -eq 280) "computed=$($req1.computedCoverage)"
Check "Capped amount = 280 (under cap)"        ([decimal]$req1.cappedAmount -eq 280) "capped=$($req1.cappedAmount)"
Check "Cap not reached"                        ($req1.capReached -eq $false)
Check "Status = Submitted"                     ($req1.status -eq 'Submitted') "status=$($req1.status)"

$sr = Api POST "/api/admin/compensation-requests/$($req1.id)/start-review" $admin
Check "Admin start-review (-> UnderReview)"    ($sr.Status -eq 204)
$ap = Api POST "/api/admin/compensation-requests/$($req1.id)/approve" $admin @{ reviewerNote = "e2e approve" }
Check "Admin approve (-> Approved, scheduled)" ($ap.Status -eq 204)
$after = Get-AdminRequest $admin $req1.id
Check "Approved with a scheduled payout date"  ($after.status -eq 'Approved' -and $null -ne $after.scheduledPayoutDateUtc) "status=$($after.status) date=$($after.scheduledPayoutDateUtc)"

# The payout is date-gated to the 27th/28th — force it due so the job picks it up now.
Sql "UPDATE compensation_requests SET scheduled_payout_date_utc = now() - interval '1 day' WHERE id = '$($req1.id)'" | Out-Null
$job = Api POST '/api/admin/compensation-requests/run-payout-job' $admin
Check "Run payout job (paidCount >= 1)"        ($job.Status -eq 200 -and [int]$job.Body.paidCount -ge 1) "paidCount=$($job.Body.paidCount)"
$paid = Get-AdminRequest $admin $req1.id
Check "Request now Paid"                        ($paid.status -eq 'Paid') "status=$($paid.status)"
$a1b = Get-Account $t1.Token
Check "Stage dropped Stage1 -> Rebuild"        ($a1b.currentStage -eq 'Rebuild') "stage=$($a1b.currentStage)"
Check "Compensation bucket credited 280 (once)" ([decimal]$a1b.composition.compensation -eq 280) "compensation=$($a1b.composition.compensation)"
Check "Total balance now 2280"                 ([decimal]$a1b.composition.total -eq 2280) "total=$($a1b.composition.total)"

# ------------------------------------------------ 4. reconciliation CONFIRM (simulated crash)

Section "4. Reconciliation CONFIRM — stuck 'Paying' -> confirm-paid (trader t2)"
$t2 = New-Trader 't2'
$claim2 = Approve-Claim $t2 $admin 1000 0     # 50% of 1000 = 500
# Simulate a payout run that credited MT5 then died before committing: force the request to Paying.
Sql "UPDATE compensation_requests SET status = 'Paying' WHERE id = '$($claim2.Id)'" | Out-Null
$inflight = Api GET '/api/admin/compensation-requests/queue/in-flight' $admin
Check "Stuck request shows in in-flight queue" ($inflight.Status -eq 200 -and ($inflight.Body | Where-Object { $_.id -eq $claim2.Id })) "count=$(($inflight.Body | Measure-Object).Count)"
$conf = Api POST "/api/admin/compensation-requests/$($claim2.Id)/reconcile/confirm-paid" $admin
Check "confirm-paid succeeds (204)"            ($conf.Status -eq 204) "HTTP $($conf.Status)"
$r2 = Get-AdminRequest $admin $claim2.Id
Check "Request now Paid"                        ($r2.status -eq 'Paid') "status=$($r2.status)"
$a2 = Get-Account $t2.Token
Check "Stage dropped to Rebuild"               ($a2.currentStage -eq 'Rebuild') "stage=$($a2.currentStage)"
Check "Compensation credited 500 exactly once" ([decimal]$a2.composition.compensation -eq 500) "compensation=$($a2.composition.compensation)"

# ------------------------------------------------ 5. reconciliation RESET (simulated crash)

Section "5. Reconciliation RESET — stuck 'Paying' -> reset-approved -> re-paid (trader t3)"
$t3 = New-Trader 't3'
$claim3 = Approve-Claim $t3 $admin 200 0      # 50% of 200 = 100
Sql "UPDATE compensation_requests SET status = 'Paying' WHERE id = '$($claim3.Id)'" | Out-Null
$reset = Api POST "/api/admin/compensation-requests/$($claim3.Id)/reconcile/reset-approved" $admin
Check "reset-approved succeeds (204)"          ($reset.Status -eq 204) "HTTP $($reset.Status)"
$r3 = Get-AdminRequest $admin $claim3.Id
Check "Request back to Approved"               ($r3.status -eq 'Approved') "status=$($r3.status)"
# Now let the job pay it cleanly.
Sql "UPDATE compensation_requests SET scheduled_payout_date_utc = now() - interval '1 day' WHERE id = '$($claim3.Id)'" | Out-Null
Api POST '/api/admin/compensation-requests/run-payout-job' $admin | Out-Null
$r3b = Get-AdminRequest $admin $claim3.Id
Check "Request now Paid by the job"            ($r3b.status -eq 'Paid') "status=$($r3b.status)"
$a3 = Get-Account $t3.Token
Check "Compensation credited 100 exactly once" ([decimal]$a3.composition.compensation -eq 100) "compensation=$($a3.composition.compensation)"

# ------------------------------------------------ 6. reconciliation guard

Section "6. Reconciliation guard — confirm-paid only valid from 'Paying'"
$guard = Api POST "/api/admin/compensation-requests/$($req1.id)/reconcile/confirm-paid" $admin  # req1 is already Paid
Check "confirm-paid on a Paid request rejected (400)" ($guard.Status -eq 400) "HTTP $($guard.Status)"

# ------------------------------------------------ 7. lifetime cap clamp

Section "7. `$5,000 lifetime cap clamp (trader t4)"
$t4 = New-Trader 't4'
$big = Api POST '/api/compensation-requests' $t4.Token @{ totalTradingLoss = 12000; commissionPaid = 0 }  # 50% = 6000
Check "Submit large claim (loss 12000)"        ($big.Status -eq 200) "HTTP $($big.Status)"
Check "Computed coverage = 6000"               ([decimal]$big.Body.computedCoverage -eq 6000) "computed=$($big.Body.computedCoverage)"
Check "Capped amount clamped to 5000"          ([decimal]$big.Body.cappedAmount -eq 5000) "capped=$($big.Body.cappedAmount)"
Check "capReached = true"                       ($big.Body.capReached -eq $true)

# ------------------------------------------------ 8. profit withdrawal + admin completion

Section "8. Profit withdrawal + admin completion (trader t5)"
$t5 = New-Trader 't5'
Sql "UPDATE shield_accounts SET composition_profit = 1000 WHERE id = '$($t5.AccountId)'" | Out-Null   # Stub has no trades; inject profit
$wd = Api POST '/api/withdrawals' $t5.Token @{ requestedAmount = 500 }
Check "Request withdrawal of 500"              ($wd.Status -eq 200) "HTTP $($wd.Status)"
$w = $wd.Body
Check "Profit portion = 500"                    ([decimal]$w.profitPortion -eq 500) "profitPortion=$($w.profitPortion)"
Check "Broker share = 150 (Stage1 30%)"        ([decimal]$w.brokerShareAmount -eq 150) "brokerShare=$($w.brokerShareAmount)"
Check "Net to trader = 350"                     ([decimal]$w.netToTrader -eq 350) "net=$($w.netToTrader)"
Check "Status = Requested"                      ($w.status -eq 'Requested') "status=$($w.status)"
$pending = Api GET '/api/admin/withdrawals/queue/pending' $admin
Check "Appears in admin pending queue"         ($pending.Status -eq 200 -and ($pending.Body | Where-Object { $_.id -eq $w.id })) "count=$(($pending.Body | Measure-Object).Count)"
$done = Api POST "/api/admin/withdrawals/$($w.id)/complete" $admin
Check "Admin marks complete (204)"             ($done.Status -eq 204) "HTTP $($done.Status)"
$wAfter = Api GET "/api/admin/withdrawals/$($w.id)" $admin
Check "Withdrawal now Completed"               ($wAfter.Body.status -eq 'Completed') "status=$($wAfter.Body.status)"

# ------------------------------------------------ 9. stage ladder up

Section "9. Stage ladder up — Stage1 -> Stage2 -> Stage3 -> VIP -> rejected at top (trader t6)"
$t6 = New-Trader 't6'
$acc6 = $t6.AccountId
$u1 = Api POST "/api/admin/accounts/$acc6/level-up" $admin
Check "Level up -> Stage2" ($u1.Status -eq 200 -and $u1.Body.currentStage -eq 'Stage2') "HTTP $($u1.Status) stage=$($u1.Body.currentStage)"
$u2 = Api POST "/api/admin/accounts/$acc6/level-up" $admin
Check "Level up -> Stage3" ($u2.Status -eq 200 -and $u2.Body.currentStage -eq 'Stage3') "stage=$($u2.Body.currentStage)"
$u3 = Api POST "/api/admin/accounts/$acc6/level-up" $admin
Check "Level up -> Vip"    ($u3.Status -eq 200 -and $u3.Body.currentStage -eq 'Vip') "stage=$($u3.Body.currentStage)"
$u4 = Api POST "/api/admin/accounts/$acc6/level-up" $admin
Check "Level up at VIP rejected (400)" ($u4.Status -eq 400) "HTTP $($u4.Status)"

# ------------------------------------------------ 10. compensation reject

Section "10. Compensation reject — no stage change (trader t7)"
$t7 = New-Trader 't7'
$sub7 = Api POST '/api/compensation-requests' $t7.Token @{ totalTradingLoss = 300; commissionPaid = 0 }
Check "Submit claim" ($sub7.Status -eq 200) "HTTP $($sub7.Status)"
$id7 = $sub7.Body.id
Api POST "/api/admin/compensation-requests/$id7/start-review" $admin | Out-Null
$rej = Api POST "/api/admin/compensation-requests/$id7/reject" $admin @{ reviewerNote = "insufficient evidence" }
Check "Admin reject (204)" ($rej.Status -eq 204) "HTTP $($rej.Status)"
$r7 = Get-AdminRequest $admin $id7
Check "Request Rejected" ($r7.status -eq 'Rejected') "status=$($r7.status)"
$a7 = Get-Account $t7.Token
Check "Account stays at Stage1 (no drop)" ($a7.currentStage -eq 'Stage1') "stage=$($a7.currentStage)"

# ---------------------------------------------------------------------------- summary

Section "Summary"
$total = $script:pass + $script:fail
Write-Host ""
Write-Host ("  {0} passed, {1} failed, {2} total" -f $script:pass, $script:fail, $total) -ForegroundColor ($(if ($script:fail -eq 0) { 'Green' } else { 'Red' }))
Write-Host ""
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
