using System;

namespace BotCore.Helpers;

/// <summary>
/// Helper class for price rounding to ensure compliance with futures tick size rules.
/// ES and NQ both use 0.25 tick sizes per TopstepX requirements.
/// </summary>
public static class PriceHelper
{
    private const decimal TickSize = 0.25m;

    /// <summary>
    /// Round price to the nearest valid tick increment for the given symbol.
    /// </summary>
    /// <param name="price">The price to round</param>
    /// <param name="symbol">The futures symbol (ES or NQ)</param>
    /// <returns>Rounded price that complies with tick size rules</returns>
    /// <exception cref="ArgumentException">Thrown when symbol is not supported</exception>
    public static decimal RoundToTick(decimal price, string symbol)
    {
        // Validate supported symbols (ES and NQ only)
        if (symbol != "ES" && symbol != "NQ")
        {
            throw new ArgumentException($"Unsupported symbol: {symbol}. Only ES and NQ are supported.", nameof(symbol));
        }

        // All supported symbols use 0.25 tick size
        // Formula: Divide by tick size, round to nearest integer, multiply by tick size
        return Math.Round(price / TickSize, MidpointRounding.AwayFromZero) * TickSize;
    }
}
