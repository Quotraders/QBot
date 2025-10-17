using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace BotCore.Services;

/// <summary>
/// 🛡️ TOPSTEP COMPLIANCE MANAGER 🛡️
/// 
/// Enforces TopStep evaluation account rules to ensure compliance:
/// - Never exceed $2,400 daily loss limit (using $1,000 safety buffer)
/// - Never breach $2,500 total drawdown (using $2,000 safety buffer)
/// - Only trade approved contracts (ES and NQ full-size)
/// - Respect minimum trading days requirements
/// - Track profit targets for evaluation goals
/// - Automatically stop trading if approaching limits
/// 
/// This is critical for maintaining funded account status and passing evaluations.
/// </summary>
public class TopStepComplianceManager
{
    private readonly ILogger _logger;
    
    // TopStep compliance limits (read from environment variables)
    private readonly decimal TopStepDailyLossLimit;
    private readonly decimal SafeDailyLossLimit;
    private readonly decimal TopStepDrawdownLimit;
    private readonly decimal SafeDrawdownLimit;
    
    // Compliance threshold percentages
    private const decimal WarningThresholdPercent = 0.8m;  // 80% threshold for warning
    private const decimal CriticalThresholdPercent = 0.9m; // 90% threshold for critical
    private const decimal PercentToDecimalConversion = 100m; // Convert decimal to percentage
    
    // TopStep evaluation requirements (read from environment variables)
    private readonly decimal ProfitTargetAmount;
    private readonly int MinimumTradingDays;
    private const decimal DailyLossWarningThreshold = 200m; // Warning when remaining < $200
    private const decimal DrawdownWarningThreshold = 300m;  // Warning when remaining < $300
    
    // Timezone constants
    private const int EasternTimeOffsetHours = -5;         // EST offset from UTC
    
    // Approved contracts for TopStep evaluation
    private readonly string[] ApprovedContracts = { "ES", "NQ" };
    
    // Account state tracking
    private decimal _currentDrawdown;
    private decimal _todayPnL;
    private decimal _accountBalance = 50000m; // Starting balance
    private DateTime _lastResetDate = DateTime.Today;
    private int _tradingDaysCompleted;
    private readonly object _stateLock = new();
    
    public TopStepComplianceManager(ILogger logger, IOptions<AutonomousConfig> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        
        _logger = logger;
        
        // Read TopStep compliance limits from environment variables (with defaults)
        TopStepDailyLossLimit = decimal.Parse(
            Environment.GetEnvironmentVariable("TOPSTEP_DAILY_LOSS_LIMIT") ?? "-2400",
            CultureInfo.InvariantCulture);
        SafeDailyLossLimit = decimal.Parse(
            Environment.GetEnvironmentVariable("TOPSTEP_SAFE_DAILY_LOSS_LIMIT") ?? "-1000",
            CultureInfo.InvariantCulture);
        TopStepDrawdownLimit = decimal.Parse(
            Environment.GetEnvironmentVariable("TOPSTEP_DRAWDOWN_LIMIT") ?? "-2500",
            CultureInfo.InvariantCulture);
        SafeDrawdownLimit = decimal.Parse(
            Environment.GetEnvironmentVariable("TOPSTEP_SAFE_DRAWDOWN_LIMIT") ?? "-2000",
            CultureInfo.InvariantCulture);
        ProfitTargetAmount = decimal.Parse(
            Environment.GetEnvironmentVariable("TOPSTEP_PROFIT_TARGET") ?? "3000",
            CultureInfo.InvariantCulture);
        MinimumTradingDays = int.Parse(
            Environment.GetEnvironmentVariable("TOPSTEP_MINIMUM_TRADING_DAYS") ?? "5",
            CultureInfo.InvariantCulture);
        
        _logger.LogInformation("🛡️ [TOPSTEP-COMPLIANCE] Initialized with safety limits: Daily=${DailyLimit}, Drawdown=${DrawdownLimit}, ProfitTarget=${ProfitTarget}, MinDays={MinDays}",
            SafeDailyLossLimit, SafeDrawdownLimit, ProfitTargetAmount, MinimumTradingDays);
    }
    
    /// <summary>
    /// Check if trading is allowed based on current compliance status
    /// Automatically switches to DRY_RUN mode if limits are breached
    /// </summary>
    public async Task<bool> CanTradeAsync(decimal currentPnL, decimal accountBalance, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_stateLock)
        {
            // Update account state
            UpdateAccountState(currentPnL, accountBalance);
            
            // Check daily loss limit - enforce DRY_RUN if breached
            if (_todayPnL <= SafeDailyLossLimit)
            {
                _logger.LogWarning("🚫 [TOPSTEP-COMPLIANCE] Daily loss limit reached: ${PnL} <= ${Limit}",
                    _todayPnL, SafeDailyLossLimit);
                
                // Automatically switch to DRY_RUN mode
                EnforceDryRunMode("TopStep safe daily loss limit reached");
                return false;
            }
            
            // Check total drawdown limit - enforce DRY_RUN if breached
            if (_currentDrawdown <= SafeDrawdownLimit)
            {
                _logger.LogWarning("🚫 [TOPSTEP-COMPLIANCE] Drawdown limit reached: ${Drawdown} <= ${Limit}",
                    _currentDrawdown, SafeDrawdownLimit);
                
                // Automatically switch to DRY_RUN mode
                EnforceDryRunMode("TopStep safe drawdown limit reached");
                return false;
            }
            
            // Check if approaching limits (warning threshold)
            if (_todayPnL <= SafeDailyLossLimit * WarningThresholdPercent)
            {
                _logger.LogWarning("⚠️ [TOPSTEP-COMPLIANCE] Approaching daily loss limit: ${PnL} ({Percent}% of ${Limit})",
                    _todayPnL, WarningThresholdPercent * PercentToDecimalConversion, SafeDailyLossLimit);
                // Still allow trading but with caution
            }
            
            if (_currentDrawdown <= SafeDrawdownLimit * WarningThresholdPercent)
            {
                _logger.LogWarning("⚠️ [TOPSTEP-COMPLIANCE] Approaching drawdown limit: ${Drawdown} ({Percent}% of ${Limit})",
                    _currentDrawdown, WarningThresholdPercent * PercentToDecimalConversion, SafeDrawdownLimit);
                // Still allow trading but with caution
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// Check if a specific contract is approved for TopStep evaluation
    /// </summary>
    public bool IsContractApproved(string symbol)
    {
        var approved = Array.Exists(ApprovedContracts, contract => 
            symbol.StartsWith(contract, StringComparison.OrdinalIgnoreCase));
        
        if (!approved)
        {
            _logger.LogWarning("🚫 [TOPSTEP-COMPLIANCE] Contract not approved: {Symbol}. Approved: {Contracts}",
                symbol, string.Join(", ", ApprovedContracts));
        }
        
        return approved;
    }
    
    /// <summary>
    /// Calculate maximum allowed position size based on remaining risk budget
    /// </summary>
    public decimal CalculateMaxAllowedPositionSize(decimal proposedSize, decimal stopDistance)
    {
        lock (_stateLock)
        {
            // Calculate remaining daily risk budget
            var remainingDailyRisk = Math.Abs(SafeDailyLossLimit - _todayPnL);
            
            // Calculate remaining drawdown risk budget
            var remainingDrawdownRisk = Math.Abs(SafeDrawdownLimit - _currentDrawdown);
            
            // Use the smaller of the two limits
            var remainingRisk = Math.Min(remainingDailyRisk, remainingDrawdownRisk);
            
            // Calculate maximum position size based on stop distance
            decimal maxPositionSize = 0m;
            if (stopDistance > 0)
            {
                maxPositionSize = remainingRisk / stopDistance;
            }
            
            // Use the smaller of proposed size or max allowed size
            var allowedSize = Math.Min(proposedSize, maxPositionSize);
            
            _logger.LogDebug("💰 [TOPSTEP-COMPLIANCE] Position sizing: Proposed=${Proposed}, Max=${Max}, Allowed=${Allowed} (Risk budget: ${Budget})",
                proposedSize, maxPositionSize, allowedSize, remainingRisk);
            
            return Math.Max(0, allowedSize);
        }
    }
    
    /// <summary>
    /// Record a trade result and update compliance tracking
    /// </summary>
    public async Task RecordTradeResultAsync(string symbol, decimal pnl, DateTime tradeTime, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        lock (_stateLock)
        {
            // Check if contract was approved
            if (!IsContractApproved(symbol))
            {
                _logger.LogError("❌ [TOPSTEP-COMPLIANCE] Trade recorded for non-approved contract: {Symbol}", symbol);
                // Could trigger compliance violation alert
            }
            
            // Update daily P&L
            if (tradeTime.Date != _lastResetDate)
            {
                // New trading day - reset daily P&L
                _todayPnL = 0m;
                _lastResetDate = tradeTime.Date;
                _tradingDaysCompleted++;
                
                _logger.LogInformation("📅 [TOPSTEP-COMPLIANCE] New trading day: {Date}, Days completed: {Days}",
                    tradeTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _tradingDaysCompleted);
            }
            
            _todayPnL += pnl;
            
            // Update drawdown if this is a loss
            if (pnl < 0)
            {
                _currentDrawdown = Math.Min(_currentDrawdown, _todayPnL);
            }
            
            _logger.LogDebug("📊 [TOPSTEP-COMPLIANCE] Trade recorded: {Symbol} ${PnL:F2}, Daily: ${Daily:F2}, Drawdown: ${Drawdown:F2}",
                symbol, pnl, _todayPnL, _currentDrawdown);
        }
        
        // Check for compliance violations outside the lock
        await CheckComplianceViolationsAsync().ConfigureAwait(false);
    }
    
    /// <summary>
    /// Get current compliance status
    /// </summary>
    public ComplianceStatus GetComplianceStatus()
    {
        lock (_stateLock)
        {
            return new ComplianceStatus
            {
                TodayPnL = _todayPnL,
                CurrentDrawdown = _currentDrawdown,
                AccountBalance = _accountBalance,
                TradingDaysCompleted = _tradingDaysCompleted,
                DailyLossRemaining = Math.Abs(SafeDailyLossLimit - _todayPnL),
                DrawdownRemaining = Math.Abs(SafeDrawdownLimit - _currentDrawdown),
                IsCompliant = _todayPnL > SafeDailyLossLimit && _currentDrawdown > SafeDrawdownLimit,
                DaysUntilMinimum = Math.Max(0, GetMinimumTradingDays() - _tradingDaysCompleted)
            };
        }
    }
    
    /// <summary>
    /// Get profit target for TopStep evaluation
    /// </summary>
    public decimal ProfitTarget =>
        // TopStep evaluation profit targets
        // Evaluation: $3,000 profit target for $50K account
        // Funded: No specific target, but consistent profitability expected
        ProfitTargetAmount;
    
    /// <summary>
    /// Check if minimum trading days requirement is met
    /// </summary>
    public bool IsMinimumTradingDaysMet()
    {
        var minimumDays = GetMinimumTradingDays();
        return _tradingDaysCompleted >= minimumDays;
    }
    
    /// <summary>
    /// Get time until daily reset (5 PM ET)
    /// </summary>
    public static TimeSpan GetTimeUntilDailyReset()
    {
        var easternTime = GetEasternTime();
        var resetTime = easternTime.Date.AddHours(17); // 5 PM ET
        
        if (easternTime.TimeOfDay >= TimeSpan.FromHours(17))
        {
            resetTime = resetTime.AddDays(1); // Next day's reset
        }
        
        return resetTime - easternTime;
    }
    
    /// <summary>
    /// Generate compliance report
    /// </summary>
    public async Task<ComplianceReport> GenerateComplianceReportAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        
        var status = GetComplianceStatus();
        var profitTarget = ProfitTarget;
        var progressToTarget = (status.AccountBalance - 50000m) / profitTarget * 100m;
        
        // Build recommendations list first
        var recommendations = GenerateRecommendations(status);
        
        var report = new ComplianceReport
        {
            Date = DateTime.Today,
            Status = status,
            ProfitTarget = profitTarget,
            ProgressToTarget = Math.Max(0, progressToTarget),
            IsOnTrack = status.IsCompliant && progressToTarget > 0,
            MinimumDaysRemaining = Math.Max(0, GetMinimumTradingDays() - _tradingDaysCompleted),
            Recommendations = recommendations.ToList()
        };
        
        return report;
    }
    
    private void UpdateAccountState(decimal currentPnL, decimal accountBalance)
    {
        // Update account balance
        _accountBalance = accountBalance;
        
        // Reset daily P&L if new day
        var today = DateTime.Today;
        if (_lastResetDate != today)
        {
            _todayPnL = 0m;
            _lastResetDate = today;
            _tradingDaysCompleted++;
            
            _logger.LogInformation("📅 [TOPSTEP-COMPLIANCE] Daily reset: {Date}, Trading days: {Days}",
                today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _tradingDaysCompleted);
        }
        
        _todayPnL = currentPnL;
        
        // Update drawdown tracking
        var accountHighWaterMark = Math.Max(50000m, _accountBalance); // Assuming we track high water mark
        _currentDrawdown = _accountBalance - accountHighWaterMark;
    }
    
    private Task CheckComplianceViolationsAsync()
    {
        // Check for hard violations - automatically switch to DRY_RUN mode
        if (_todayPnL <= TopStepDailyLossLimit)
        {
            _logger.LogCritical("🚨 [TOPSTEP-COMPLIANCE] VIOLATION: Daily loss limit exceeded: ${PnL} <= ${Limit}",
                _todayPnL, TopStepDailyLossLimit);
            
            // Automatically enforce DRY_RUN mode to protect account
            EnforceDryRunMode("TopStep daily loss limit exceeded");
        }
        
        if (_currentDrawdown <= TopStepDrawdownLimit)
        {
            _logger.LogCritical("🚨 [TOPSTEP-COMPLIANCE] VIOLATION: Drawdown limit exceeded: ${Drawdown} <= ${Limit}",
                _currentDrawdown, TopStepDrawdownLimit);
            
            // Automatically enforce DRY_RUN mode to protect account
            EnforceDryRunMode("TopStep drawdown limit exceeded");
        }
        
        // Check for approaching violations (critical threshold) - also switch to DRY_RUN as safety measure
        if (_todayPnL <= TopStepDailyLossLimit * CriticalThresholdPercent)
        {
            _logger.LogError("🔴 [TOPSTEP-COMPLIANCE] CRITICAL: Approaching daily loss limit: ${PnL}",
                _todayPnL);
            
            // Switch to DRY_RUN mode before hitting hard limit
            EnforceDryRunMode("Approaching TopStep daily loss limit (90% threshold)");
        }
        
        if (_currentDrawdown <= TopStepDrawdownLimit * CriticalThresholdPercent)
        {
            _logger.LogError("🔴 [TOPSTEP-COMPLIANCE] CRITICAL: Approaching drawdown limit: ${Drawdown}",
                _currentDrawdown);
            
            // Switch to DRY_RUN mode before hitting hard limit
            EnforceDryRunMode("Approaching TopStep drawdown limit (90% threshold)");
        }

        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Enforce DRY_RUN mode when compliance limits are breached
    /// Switches bot to paper trading with live data (no real trades)
    /// </summary>
    private void EnforceDryRunMode(string reason)
    {
        try
        {
            // Set DRY_RUN=1 to switch to paper trading mode (live data but simulated trades)
            Environment.SetEnvironmentVariable("DRY_RUN", "1");
            
            _logger.LogCritical("🛡️ [TOPSTEP-COMPLIANCE] DRY_RUN MODE ENFORCED - Reason: {Reason}", reason);
            _logger.LogCritical("🛡️ [TOPSTEP-COMPLIANCE] Bot switched to paper trading - continues with live data but simulates trades");
            _logger.LogInformation("💡 [TOPSTEP-COMPLIANCE] To resume live trading: Review account status, reset if needed, and set DRY_RUN=0");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [TOPSTEP-COMPLIANCE] Failed to enforce DRY_RUN mode");
        }
    }
    
    private int GetMinimumTradingDays()
    {
        // TopStep evaluation requirements
        // Evaluation: Minimum 5 trading days
        // Express Funded: Minimum 5 trading days
        return MinimumTradingDays;
    }
    
    private static DateTime GetEasternTime()
    {
        try
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone);
        }
        catch
        {
            // Fallback to UTC-5 (EST) if timezone not found
            return DateTime.UtcNow.AddHours(EasternTimeOffsetHours);
        }
    }
    
    private List<string> GenerateRecommendations(ComplianceStatus status)
    {
        var recommendations = new List<string>();
        
        if (!status.IsCompliant)
        {
            recommendations.Add("COMPLIANCE VIOLATION: Bot automatically switched to DRY_RUN mode (paper trading with live data)");
            recommendations.Add("Review account status and resolve issues before resuming live trading");
            return recommendations;
        }
        
        if (status.DailyLossRemaining < DailyLossWarningThreshold)
        {
            recommendations.Add("Reduce position size - approaching daily loss limit");
            recommendations.Add("Bot will automatically switch to DRY_RUN mode if limit is reached");
        }
        
        if (status.DrawdownRemaining < DrawdownWarningThreshold)
        {
            recommendations.Add("Consider defensive trading - approaching drawdown limit");
            recommendations.Add("Bot will automatically switch to DRY_RUN mode if limit is reached");
        }
        
        if (status.DaysUntilMinimum > 0)
        {
            recommendations.Add($"Continue trading for {status.DaysUntilMinimum} more days to meet minimum requirement");
        }
        
        var profitTarget = ProfitTarget;
        var remainingTarget = profitTarget - (status.AccountBalance - 50000m);
        if (remainingTarget > 0)
        {
            recommendations.Add($"${remainingTarget:F0} remaining to reach profit target");
        }
        
        return recommendations;
    }
}

/// <summary>
/// Current compliance status
/// </summary>
public class ComplianceStatus
{
    public decimal TodayPnL { get; set; }
    public decimal CurrentDrawdown { get; set; }
    public decimal AccountBalance { get; set; }
    public int TradingDaysCompleted { get; set; }
    public decimal DailyLossRemaining { get; set; }
    public decimal DrawdownRemaining { get; set; }
    public bool IsCompliant { get; set; }
    public int DaysUntilMinimum { get; set; }
}

/// <summary>
/// Compliance report for monitoring and analysis
/// </summary>
public class ComplianceReport
{
    public DateTime Date { get; set; }
    public ComplianceStatus Status { get; set; } = new();
    public decimal ProfitTarget { get; set; }
    public decimal ProgressToTarget { get; set; }
    public bool IsOnTrack { get; set; }
    public int MinimumDaysRemaining { get; set; }
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
}