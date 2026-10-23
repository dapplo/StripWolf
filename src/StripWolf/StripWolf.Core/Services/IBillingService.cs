using System.Threading;
using System.Threading.Tasks;

namespace StripWolf.Core.Services;

/// <summary>
/// Platform-specific billing interface to verify Google Play/iOS purchases.
/// </summary>
public interface IBillingService
{
    /// <summary>
    /// Queries the platform's app store to check if the premium unlock product is owned.
    /// Returns false only when the store confirms the product isn't owned. When the store can't be reached or the
    /// answer is unknown, throw: TrialService then keeps the stored unlock, so a paying user offline is never relocked.
    /// </summary>
    Task<bool> QueryPremiumPurchaseAsync(CancellationToken cancellationToken);
}
