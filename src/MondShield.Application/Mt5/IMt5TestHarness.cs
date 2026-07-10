namespace MondShield.Application.Mt5;

/// <summary>
/// Test/dev seam implemented ONLY by the in-memory stub (<c>Mt5StubClient</c>) — never by the live
/// Manager client. Lets a Development-only endpoint simulate money moving on MT5 outside our flows
/// (a trader top-up / manual dealer op) so the reconcile → capture → pending-review → classify
/// pipeline can be exercised end-to-end without a live server. A live build won't implement this,
/// so the guarding endpoint returns 404 in production.
/// </summary>
public interface IMt5TestHarness
{
    /// <summary>Records an external balance change on a stub login (positive = deposit, negative = withdrawal).</summary>
    void SimulateExternalBalanceOperation(long login, decimal amount, string comment);
}
