using System;

namespace BotCore.DecimalMath;

/// <summary>
/// Decimal-safe mathematical operations for financial calculations
/// Provides precision-preserving alternatives to System.Math methods that require double
/// </summary>
public static class MathHelper
{
    /// <summary>
    /// Calculate square root of a decimal value
    /// NOTE: Converts to double internally - acceptable precision loss for financial calculations
    /// Precision: ~15-16 significant digits maintained
    /// </summary>
    public static decimal Sqrt(decimal value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Cannot calculate square root of negative number");
        }
        
        if (value == 0)
        {
            return 0m;
        }
        
        // Convert to double, calculate, convert back
        // Document acceptable precision loss for √x where x is typically small price differences
        return (decimal)System.Math.Sqrt((double)value);
    }
    
    /// <summary>
    /// Calculate power (base^exponent) with decimal values
    /// NOTE: Converts to double internally - acceptable precision loss for financial calculations
    /// Precision: ~15-16 significant digits maintained
    /// </summary>
    public static decimal Pow(decimal baseValue, decimal exponent)
    {
        // Convert to double, calculate, convert back
        // Document acceptable precision loss for typical trading calculations
        return (decimal)System.Math.Pow((double)baseValue, (double)exponent);
    }
    
    /// <summary>
    /// Calculate absolute value - native decimal operation (no precision loss)
    /// </summary>
    public static decimal Abs(decimal value)
    {
        return System.Math.Abs(value);
    }
    
    /// <summary>
    /// Calculate maximum of two decimal values - native decimal operation (no precision loss)
    /// </summary>
    public static decimal Max(decimal val1, decimal val2)
    {
        return System.Math.Max(val1, val2);
    }
    
    /// <summary>
    /// Calculate minimum of two decimal values - native decimal operation (no precision loss)
    /// </summary>
    public static decimal Min(decimal val1, decimal val2)
    {
        return System.Math.Min(val1, val2);
    }
    
    /// <summary>
    /// Round to specified number of decimal places - native decimal operation (no precision loss)
    /// </summary>
    public static decimal Round(decimal value, int decimals)
    {
        return System.Math.Round(value, decimals);
    }
    
    /// <summary>
    /// Calculate exponential (e^x) with decimal value
    /// NOTE: Converts to double internally - acceptable precision loss
    /// </summary>
    public static decimal Exp(decimal value)
    {
        return (decimal)System.Math.Exp((double)value);
    }
    
    /// <summary>
    /// Calculate natural logarithm with decimal value
    /// NOTE: Converts to double internally - acceptable precision loss
    /// </summary>
    public static decimal Log(decimal value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Cannot calculate logarithm of non-positive number");
        }
        
        return (decimal)System.Math.Log((double)value);
    }
}
