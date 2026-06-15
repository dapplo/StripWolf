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
    /// </summary>
    Task<bool> QueryPremiumPurchaseAsync(CancellationToken cancellationToken);
}
