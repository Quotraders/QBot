using TradingBot.Abstractions;

// This file now serves as a simple bridge to the contracts in Abstractions
// All S7 contracts are now properly located in TradingBot.Abstractions namespace
// to avoid circular dependencies

namespace TradingBot.S7
{
    // Re-export types for backward compatibility within S7 namespace
    using S7State = TradingBot.Abstractions.S7State;
    using S7Leader = TradingBot.Abstractions.S7Leader;
    using S7Snapshot = TradingBot.Abstractions.S7Snapshot;
    using S7FeatureTuple = TradingBot.Abstractions.S7FeatureTuple;
    using S7Configuration = TradingBot.Abstractions.S7Configuration;
    using IS7Service = TradingBot.Abstractions.IS7Service;
    using IS7FeatureSource = TradingBot.Abstractions.IS7FeatureSource;
    using BreadthConfiguration = TradingBot.Abstractions.BreadthConfiguration;
    using IBreadthFeed = TradingBot.Abstractions.IBreadthFeed;
}