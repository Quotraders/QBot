using System;
using System.Collections.Generic;

namespace BotCore.Models
{
    /// <summary>
    /// Data structures for advanced order types (OCO, Bracket, Iceberg)
    /// Extracted to separate file for better organization
    /// </summary>
    
    internal sealed class OcoOrderPair
    {
        public string OcoId { get; set; } = string.Empty;
        public string OrderId1 { get; set; } = string.Empty;
        public string OrderId2 { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public OcoStatus Status { get; set; } = OcoStatus.Active;
        public string? FilledOrderId { get; set; }
        public string? CancelledOrderId { get; set; }
    }
    
    internal enum OcoStatus
    {
        Active,
        OneFilled,
        BothCancelled,
        Expired
    }
    
    internal sealed class BracketOrderGroup
    {
        public string BracketId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string? EntryOrderId { get; set; }
        public string? StopOrderId { get; set; }
        public string? TargetOrderId { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal StopPrice { get; set; }
        public decimal TargetPrice { get; set; }
        public int Quantity { get; set; }
        public string Side { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public BracketStatus Status { get; set; } = BracketStatus.Pending;
    }
    
    internal enum BracketStatus
    {
        Pending,
        EntryFilled,
        StopFilled,
        TargetFilled,
        Cancelled,
        Error
    }
    
    internal sealed class IcebergOrderExecution
    {
        public string IcebergId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public int TotalQuantity { get; set; }
        public int DisplayQuantity { get; set; }
        public int FilledQuantity { get; set; }
        public decimal? LimitPrice { get; set; }
        public DateTime CreatedAt { get; set; }
        public IcebergStatus Status { get; set; } = IcebergStatus.Active;
        public List<string> ChildOrderIds { get; } = new();
        public decimal? InitialMarketPrice { get; set; }
        public decimal MaxSlippageTicks { get; set; } = 2.0m;
    }
    
    internal enum IcebergStatus
    {
        Active,
        Completed,
        Cancelled,
        Error
    }
}
