using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BotCore.Configuration;

namespace BotCore.Services
{
    /// <summary>
    /// Contract rollover service for automatic front month detection and management
    /// Handles ES, NQ contract rollovers to ensure trading on active contracts
    /// </summary>
    public interface IContractRolloverService
    {
        Task<string> GetCurrentFrontMonthContractAsync(string baseSymbol);
        Task<ContractInfo> GetContractInfoAsync(string contractSymbol);
        Task<bool> ShouldRolloverAsync(string currentContract);
        Task<string> GetNextContractAsync(string currentContract);
        Task<List<ContractInfo>> GetActiveContractsAsync(string baseSymbol);
        DateTime GetContractExpirationDate(string contractSymbol);
        Task MonitorRolloverRequirementsAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Comprehensive contract rollover service implementation
    /// </summary>
    public class ContractRolloverService : IContractRolloverService
    {
        private readonly ILogger<ContractRolloverService> _logger;
        private readonly DataFlowEnhancementConfiguration _config;
        private readonly Dictionary<string, ContractSpec> _contractSpecs;
        
        // Contract rollover thresholds
        private const int FrontMonthMaxDaysToExpiration = 60;
        private const int MonthsAheadForActiveContracts = 12;
        private const int DaysAfterFirstFridayForThirdFriday = 14;
        private const int YearModuloForTwoDigitYear = 100;
        
        // ES contract specifications
        private const decimal EsTickSize = 0.25m;
        private const int EsContractSize = 50;
        
        // NQ contract specifications
        private const decimal NqTickSize = 0.25m;
        private const int NqContractSize = 20;
        
        // Month number constants for futures contract codes
        private const int JanuaryMonth = 1;
        private const int FebruaryMonth = 2;
        private const int MarchMonth = 3;
        private const int AprilMonth = 4;
        private const int MayMonth = 5;
        private const int JuneMonth = 6;
        private const int JulyMonth = 7;
        private const int AugustMonth = 8;
        private const int SeptemberMonth = 9;
        private const int OctoberMonth = 10;
        private const int NovemberMonth = 11;
        private const int DecemberMonth = 12;
        
        // Contract symbol parsing constants
        private const int BaseSymbolLength = 2; // ES, NQ are 2 characters
        private const int MinContractSymbolLengthForYear = 3; // Base + month code + year
        private const int YearDigits = 2; // Two-digit year in contract symbols
        private const int YearThresholdForCenturyAdjustment = 50; // Years more than 50 in past get next century
        private const int CenturyDivisor = 100; // For century calculations

        public ContractRolloverService(
            ILogger<ContractRolloverService> logger,
            IOptions<DataFlowEnhancementConfiguration> config)
        {
            _logger = logger;
            ArgumentNullException.ThrowIfNull(config);
            _config = config.Value;
            _contractSpecs = InitializeContractSpecs();
        }

        /// <summary>
        /// Get current front month contract for a base symbol
        /// </summary>
        public async Task<string> GetCurrentFrontMonthContractAsync(string baseSymbol)
        {
            ArgumentException.ThrowIfNullOrEmpty(baseSymbol);

            try
            {
                _logger.LogDebug("[CONTRACT-ROLLOVER] Getting front month contract for {BaseSymbol}", baseSymbol);

                // Check configured mapping first
                if (_config.FrontMonthMapping.TryGetValue(baseSymbol.ToUpper(), out var configuredContract))
                {
                    // Verify the configured contract is still valid
                    if (await IsContractActiveAsync(configuredContract).ConfigureAwait(false))
                    {
                        return configuredContract;
                    }
                    else
                    {
                        _logger.LogWarning("[CONTRACT-ROLLOVER] Configured contract {Contract} is no longer active, calculating new front month", configuredContract);
                    }
                }

                // Calculate front month based on current date
                var frontMonth = CalculateFrontMonthContract(baseSymbol);
                
                _logger.LogInformation("[CONTRACT-ROLLOVER] Front month contract for {BaseSymbol}: {FrontMonth}", baseSymbol, frontMonth);
                
                return frontMonth;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[CONTRACT-ROLLOVER] Invalid argument getting front month contract for {BaseSymbol}", baseSymbol);
                
                // Return fallback
                var fallback = _config.FrontMonthMapping.TryGetValue(baseSymbol.ToUpper(CultureInfo.InvariantCulture), out var fb) 
                    ? fb 
                    : $"{baseSymbol}Z25"; // Default to December 2025
                return fallback;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[CONTRACT-ROLLOVER] Invalid operation getting front month contract for {BaseSymbol}", baseSymbol);
                
                // Return fallback
                var fallback = _config.FrontMonthMapping.TryGetValue(baseSymbol.ToUpper(CultureInfo.InvariantCulture), out var fb) 
                    ? fb 
                    : $"{baseSymbol}Z25"; // Default to December 2025
                return fallback;
            }
        }

        /// <summary>
        /// Get detailed contract information
        /// </summary>
        public async Task<ContractInfo> GetContractInfoAsync(string contractSymbol)
        {
            ArgumentException.ThrowIfNullOrEmpty(contractSymbol);

            try
            {
                var baseSymbol = ExtractBaseSymbol(contractSymbol);
                var monthCode = ExtractMonthCode(contractSymbol);
                var year = ExtractYear(contractSymbol);

                if (!_contractSpecs.TryGetValue(baseSymbol, out var spec))
                {
                    throw new ArgumentException($"Unknown contract base symbol: {baseSymbol}");
                }

                var expirationDate = CalculateExpirationDate(monthCode, year);
                var isActive = await IsContractActiveAsync(contractSymbol).ConfigureAwait(false);
                var daysToExpiration = (expirationDate - DateTime.UtcNow).Days;

                return new ContractInfo
                {
                    ContractSymbol = contractSymbol,
                    BaseSymbol = baseSymbol,
                    MonthCode = monthCode,
                    Year = year,
                    ExpirationDate = expirationDate,
                    DaysToExpiration = daysToExpiration,
                    IsActive = isActive,
                    IsFrontMonth = daysToExpiration > 0 && daysToExpiration <= FrontMonthMaxDaysToExpiration, // Simplified logic
                    TickSize = spec.TickSize,
                    ContractSize = spec.ContractSize,
                    Currency = spec.Currency
                };
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[CONTRACT-INFO] Invalid argument getting contract info for {ContractSymbol}", contractSymbol);
                throw new InvalidOperationException($"Failed to get contract info for {contractSymbol} due to invalid argument", ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[CONTRACT-INFO] Invalid operation getting contract info for {ContractSymbol}", contractSymbol);
                throw new InvalidOperationException($"Failed to get contract info for {contractSymbol}", ex);
            }
        }

        /// <summary>
        /// Determine if a contract should be rolled over
        /// </summary>
        public async Task<bool> ShouldRolloverAsync(string currentContract)
        {
            try
            {
                if (!_config.EnableContractRollover)
                    return false;

                var contractInfo = await GetContractInfoAsync(currentContract).ConfigureAwait(false);
                var shouldRollover = contractInfo.DaysToExpiration <= _config.ContractRolloverDays;

                if (shouldRollover)
                {
                    _logger.LogInformation("[CONTRACT-ROLLOVER] Contract {Contract} should be rolled over ({Days} days to expiration)",
                        currentContract, contractInfo.DaysToExpiration);
                }

                return shouldRollover;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[CONTRACT-ROLLOVER] Invalid argument checking rollover for {Contract}", currentContract);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[CONTRACT-ROLLOVER] Invalid operation checking rollover for {Contract}", currentContract);
                return false;
            }
        }

        /// <summary>
        /// Get the next contract after the current one
        /// </summary>
        public Task<string> GetNextContractAsync(string currentContract)
        {
            ArgumentException.ThrowIfNullOrEmpty(currentContract);

            try
            {
                var baseSymbol = ExtractBaseSymbol(currentContract);
                var currentMonthCode = ExtractMonthCode(currentContract);
                var currentYear = ExtractYear(currentContract);

                // Get the contract spec for month sequence
                if (!_contractSpecs.TryGetValue(baseSymbol, out var spec))
                {
                    throw new ArgumentException($"Unknown contract base symbol: {baseSymbol}");
                }

                // Find current month in the sequence
                var currentIndex = Array.IndexOf(spec.MonthSequence, currentMonthCode);
                if (currentIndex == -1)
                {
                    throw new ArgumentException($"Invalid month code {currentMonthCode} for {baseSymbol}");
                }

                // Get next month
                string nextMonthCode;
                int nextYear;

                if (currentIndex < spec.MonthSequence.Length - 1)
                {
                    // Next month in same year
                    nextMonthCode = spec.MonthSequence[currentIndex + 1];
                    nextYear = currentYear;
                }
                else
                {
                    // First month of next year
                    nextMonthCode = spec.MonthSequence[0];
                    nextYear = currentYear + 1;
                }

                var nextContract = $"{baseSymbol}{nextMonthCode}{nextYear % 100:D2}";
                
                _logger.LogInformation("[CONTRACT-ROLLOVER] Next contract after {Current}: {Next}", currentContract, nextContract);
                
                return Task.FromResult(nextContract);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[CONTRACT-ROLLOVER] Invalid argument getting next contract for {Current}", currentContract);
                throw new InvalidOperationException($"Failed to get next contract after {currentContract} due to invalid argument", ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[CONTRACT-ROLLOVER] Invalid operation getting next contract for {Current}", currentContract);
                throw new InvalidOperationException($"Failed to get next contract after {currentContract}", ex);
            }
        }

        /// <summary>
        /// Get list of active contracts for a base symbol
        /// </summary>
        public async Task<List<ContractInfo>> GetActiveContractsAsync(string baseSymbol)
        {
            ArgumentException.ThrowIfNullOrEmpty(baseSymbol);
            try
            {
                var activeContracts = new List<ContractInfo>();

                if (!_contractSpecs.TryGetValue(baseSymbol.ToUpper(), out var spec))
                {
                    _logger.LogWarning("[CONTRACT-LIST] Unknown base symbol: {BaseSymbol}", baseSymbol);
                    return activeContracts;
                }

                var currentDate = DateTime.UtcNow;
                var currentYear = currentDate.Year;

                // Check contracts for current and next year
                for (int yearOffset = 0; yearOffset <= 1; yearOffset++)
                {
                    var year = currentYear + yearOffset;
                    
                    foreach (var monthCode in spec.MonthSequence)
                    {
                        var contractSymbol = $"{baseSymbol}{monthCode}{year % 100:D2}";
                        var expirationDate = CalculateExpirationDate(monthCode, year);
                        
                        // Only include contracts that haven't expired and are within 12 months
                        if (expirationDate > currentDate && expirationDate <= currentDate.AddMonths(MonthsAheadForActiveContracts))
                        {
                            var contractInfo = await GetContractInfoAsync(contractSymbol).ConfigureAwait(false);
                            activeContracts.Add(contractInfo);
                        }
                    }
                }

                return activeContracts.OrderBy(c => c.ExpirationDate).ToList();
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[CONTRACT-LIST] Invalid argument getting active contracts for {BaseSymbol}", baseSymbol);
                return new List<ContractInfo>();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[CONTRACT-LIST] Invalid operation getting active contracts for {BaseSymbol}", baseSymbol);
                return new List<ContractInfo>();
            }
        }

        /// <summary>
        /// Get contract expiration date
        /// </summary>
        public DateTime GetContractExpirationDate(string contractSymbol)
        {
            ArgumentException.ThrowIfNullOrEmpty(contractSymbol);
            _ = ExtractBaseSymbol(contractSymbol); // Base symbol validation
            var monthCode = ExtractMonthCode(contractSymbol);
            var year = ExtractYear(contractSymbol);

            return CalculateExpirationDate(monthCode, year);
        }

        /// <summary>
        /// Monitor rollover requirements continuously
        /// </summary>
        public async Task MonitorRolloverRequirementsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[CONTRACT-MONITOR] Starting contract rollover monitoring");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await CheckRolloverRequirementsAsync().ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromHours(4), cancellationToken).ConfigureAwait(false); // Check every 4 hours
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[CONTRACT-MONITOR] Contract rollover monitoring stopped");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[CONTRACT-MONITOR] Invalid argument in contract rollover monitoring");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[CONTRACT-MONITOR] Invalid operation in contract rollover monitoring");
            }
        }

        #region Private Methods

        /// <summary>
        /// Initialize contract specifications
        /// </summary>
        private static Dictionary<string, ContractSpec> InitializeContractSpecs()
        {
            return new Dictionary<string, ContractSpec>
            {
                ["ES"] = new ContractSpec
                {
                    BaseSymbol = "ES",
                    FullName = "E-mini S&P 500",
                    MonthSequence = new[] { "H", "M", "U", "Z" }, // Mar, Jun, Sep, Dec
                    TickSize = EsTickSize,
                    ContractSize = EsContractSize,
                    Currency = "USD",
                    ExpirationRule = ContractExpirationRule.ThirdFridayOfMonth
                },
                ["NQ"] = new ContractSpec
                {
                    BaseSymbol = "NQ",
                    FullName = "E-mini NASDAQ-100",
                    MonthSequence = new[] { "H", "M", "U", "Z" }, // Mar, Jun, Sep, Dec
                    TickSize = NqTickSize,
                    ContractSize = NqContractSize,
                    Currency = "USD",
                    ExpirationRule = ContractExpirationRule.ThirdFridayOfMonth
                }
            };
        }

        /// <summary>
        /// Calculate front month contract based on current date
        /// </summary>
        private string CalculateFrontMonthContract(string baseSymbol)
        {
            if (!_contractSpecs.TryGetValue(baseSymbol.ToUpper(), out var spec))
            {
                throw new ArgumentException($"Unknown base symbol: {baseSymbol}");
            }

            var currentDate = DateTime.UtcNow;
            var currentMonth = currentDate.Month;
            
            // Map months to contract months for quarterly contracts
            var quarterlyMonths = new[] { 3, 6, 9, 12 }; // Mar, Jun, Sep, Dec
            var currentQuarterIndex = quarterlyMonths.Where(m => m >= currentMonth).FirstOrDefault();
            
            if (currentQuarterIndex == 0) // Past December, go to next year March
            {
                return $"{baseSymbol}H{(currentDate.Year + 1) % YearModuloForTwoDigitYear:D2}";
            }

            var monthIndex = Array.IndexOf(quarterlyMonths, currentQuarterIndex);
            var monthCode = spec.MonthSequence[monthIndex];
            
            // Check if we're too close to expiration and should use next contract
            var expirationDate = CalculateExpirationDate(monthCode, currentDate.Year);
            if ((expirationDate - currentDate).Days <= _config.ContractRolloverDays)
            {
                // Move to next quarter
                monthIndex = (monthIndex + 1) % spec.MonthSequence.Length;
                monthCode = spec.MonthSequence[monthIndex];
                var year = monthIndex == 0 ? currentDate.Year + 1 : currentDate.Year;
                return $"{baseSymbol}{monthCode}{year % YearModuloForTwoDigitYear:D2}";
            }

            return $"{baseSymbol}{monthCode}{currentDate.Year % YearModuloForTwoDigitYear:D2}";
        }

        /// <summary>
        /// Calculate expiration date for a contract
        /// </summary>
        private static DateTime CalculateExpirationDate(string monthCode, int year)
        {
            var month = MonthCodeToMonth(monthCode);
            
            // For ES/NQ: Third Friday of the month
            var thirdFriday = GetThirdFridayOfMonth(year, month);
            
            // Set expiration time to 9:30 AM ET (market open)
            return new DateTime(thirdFriday.Year, thirdFriday.Month, thirdFriday.Day, 9, 30, 0, DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Get third Friday of a month
        /// </summary>
        private static DateTime GetThirdFridayOfMonth(int year, int month)
        {
            var firstDay = new DateTime(year, month, 1);
            var firstFriday = firstDay.AddDays((DayOfWeek.Friday - firstDay.DayOfWeek + 7) % 7);
            return firstFriday.AddDays(DaysAfterFirstFridayForThirdFriday); // Third Friday is 2 weeks after first Friday
        }

        /// <summary>
        /// Convert month code to month number
        /// </summary>
        private static int MonthCodeToMonth(string monthCode)
        {
            return monthCode.ToUpper() switch
            {
                "F" => JanuaryMonth,  // January
                "G" => FebruaryMonth,  // February
                "H" => MarchMonth,  // March
                "J" => AprilMonth,  // April
                "K" => MayMonth,  // May
                "M" => JuneMonth,  // June
                "N" => JulyMonth,  // July
                "Q" => AugustMonth,  // August
                "U" => SeptemberMonth,  // September
                "V" => OctoberMonth, // October
                "X" => NovemberMonth, // November
                "Z" => DecemberMonth, // December
                _ => throw new ArgumentException($"Invalid month code: {monthCode}")
            };
        }

        /// <summary>
        /// Extract base symbol from contract symbol (e.g., "ESZ3" -> "ES")
        /// </summary>
        private static string ExtractBaseSymbol(string contractSymbol)
        {
            if (contractSymbol.Length < BaseSymbolLength)
                throw new ArgumentException("Invalid contract symbol format");

            // Handle ES/NQ (2 chars)
            return contractSymbol[..BaseSymbolLength];
        }

        /// <summary>
        /// Extract month code from contract symbol
        /// </summary>
        private static string ExtractMonthCode(string contractSymbol)
        {
            var baseLength = ExtractBaseSymbol(contractSymbol).Length;
            if (contractSymbol.Length <= baseLength)
                throw new ArgumentException("Invalid contract symbol format");

            return contractSymbol[baseLength].ToString();
        }

        /// <summary>
        /// Extract year from contract symbol
        /// </summary>
        private static int ExtractYear(string contractSymbol)
        {
            var baseLength = ExtractBaseSymbol(contractSymbol).Length;
            if (contractSymbol.Length < baseLength + MinContractSymbolLengthForYear)
                throw new ArgumentException("Invalid contract symbol format");

            var yearStr = contractSymbol.Substring(baseLength + 1, YearDigits);
            if (!int.TryParse(yearStr, out var year))
                throw new ArgumentException("Invalid year format in contract symbol");

            // Convert 2-digit year to 4-digit year
            var currentYear = DateTime.UtcNow.Year;
            var currentCentury = (currentYear / CenturyDivisor) * CenturyDivisor;
            var fullYear = currentCentury + year;

            // If the year is more than 50 years in the past, assume next century
            if (fullYear < currentYear - YearThresholdForCenturyAdjustment)
                fullYear += CenturyDivisor;

            return fullYear;
        }

        /// <summary>
        /// Check if a contract is still active (not expired)
        /// </summary>
        private Task<bool> IsContractActiveAsync(string contractSymbol)
        {
            try
            {
                var expirationDate = GetContractExpirationDate(contractSymbol);
                return Task.FromResult(expirationDate > DateTime.UtcNow);
            }
            catch (ArgumentException)
            {
                return Task.FromResult(false);
            }
            catch (InvalidOperationException)
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Check rollover requirements for all configured contracts
        /// </summary>
        private async Task CheckRolloverRequirementsAsync()
        {
            try
            {
                var baseSymbols = new[] { "ES", "NQ" };

                foreach (var baseSymbol in baseSymbols)
                {
                    var frontMonth = await GetCurrentFrontMonthContractAsync(baseSymbol).ConfigureAwait(false);
                    var shouldRollover = await ShouldRolloverAsync(frontMonth).ConfigureAwait(false);

                    if (shouldRollover)
                    {
                        var nextContract = await GetNextContractAsync(frontMonth).ConfigureAwait(false);
                        _logger.LogWarning("[CONTRACT-MONITOR] ⚠️ Rollover required: {BaseSymbol} from {Current} to {Next}",
                            baseSymbol, frontMonth, nextContract);

                        // Update configuration mapping
                        var updatedMapping = new Dictionary<string, string>(_config.FrontMonthMapping);
                        updatedMapping[baseSymbol] = nextContract;
                        _config.ReplaceFrontMonthMapping(updatedMapping);
                    }
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[CONTRACT-MONITOR] Invalid argument checking rollover requirements");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[CONTRACT-MONITOR] Invalid operation checking rollover requirements");
            }
        }

        #endregion
    }

    #region Supporting Models

    /// <summary>
    /// Contract specification
    /// </summary>
    public class ContractSpec
    {
        public string BaseSymbol { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string[] MonthSequence { get; set; } = Array.Empty<string>();
        public decimal TickSize { get; set; }
        public int ContractSize { get; set; }
        public string Currency { get; set; } = "USD";
        public ContractExpirationRule ExpirationRule { get; set; }
    }

    /// <summary>
    /// Contract information
    /// </summary>
    public class ContractInfo
    {
        public string ContractSymbol { get; set; } = string.Empty;
        public string BaseSymbol { get; set; } = string.Empty;
        public string MonthCode { get; set; } = string.Empty;
        public int Year { get; set; }
        public DateTime ExpirationDate { get; set; }
        public int DaysToExpiration { get; set; }
        public bool IsActive { get; set; }
        public bool IsFrontMonth { get; set; }
        public decimal TickSize { get; set; }
        public int ContractSize { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    /// <summary>
    /// Contract expiration rules
    /// </summary>
    public enum ContractExpirationRule
    {
        ThirdFridayOfMonth,
        LastTradingDayOfMonth,
        CustomRule
    }

    #endregion
}