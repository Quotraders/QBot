using System.Threading;
using System.Threading.Tasks;

namespace BotCore.Features
{
    /// <summary>
    /// Interface for feature resolvers in the automation-first upgrade scope
    /// Provides OnBar and TryGet methods for real-time feature resolution
    /// All implementations must be fail-closed and audit-clean
    /// </summary>
    public interface IFeatureResolver
    {
        /// <summary>
        /// Process bar data for feature extraction
        /// Must be called on each bar boundary for real-time feature updates
        /// </summary>
        Task OnBarAsync(string symbol, object barData, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Try to get feature value for given symbol and key
        /// Returns null if feature not available - NEVER use safe defaults
        /// </summary>
        Task<double?> TryGetAsync(string symbol, string featureKey, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Get all available feature keys this resolver provides
        /// Used for feature manifest validation and audit compliance
        /// </summary>
        string[] GetAvailableFeatureKeys();
    }
}