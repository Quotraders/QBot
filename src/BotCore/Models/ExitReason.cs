namespace BotCore.Models
{
    /// <summary>
    /// Comprehensive exit reason classification for position management
    /// Critical for ML/RL optimization of stop/target placement
    /// </summary>
    public enum ExitReason
    {
        /// <summary>
        /// Exit reason not yet determined or unknown
        /// </summary>
        Unknown = 0,
        
        /// <summary>
        /// Hit profit target - planned successful exit
        /// </summary>
        Target = 1,
        
        /// <summary>
        /// Hit stop loss - planned protective exit
        /// </summary>
        StopLoss = 2,
        
        /// <summary>
        /// Moved to breakeven and stopped out at entry
        /// </summary>
        Breakeven = 3,
        
        /// <summary>
        /// Trailing stop hit - locked in profits
        /// </summary>
        TrailingStop = 4,
        
        /// <summary>
        /// Time limit exceeded - position held too long
        /// </summary>
        TimeLimit = 5,
        
        /// <summary>
        /// Zone broken - technical invalidation
        /// </summary>
        ZoneBreak = 6,
        
        /// <summary>
        /// FEATURE 3: Regime change - market conditions changed unfavorably
        /// </summary>
        RegimeChange = 11,
        
        /// <summary>
        /// Emergency exit - risk violation or system issue
        /// </summary>
        Emergency = 7,
        
        /// <summary>
        /// Manual close by user or operator
        /// </summary>
        Manual = 8,
        
        /// <summary>
        /// End of session - forced close before market close
        /// </summary>
        SessionEnd = 9,
        
        /// <summary>
        /// Partial exit - scaled out portion of position
        /// </summary>
        Partial = 10
    }
}
