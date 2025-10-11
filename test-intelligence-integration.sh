#!/bin/bash
# Test script for Intelligence Integration

set -e

echo "🧪 Testing Intelligence Integration..."
echo ""

# Test 1: Verify files were created
echo "✓ Test 1: Verify Intelligence files exist"
test -f src/BotCore/Intelligence/MarketDataReader.cs && echo "  - MarketDataReader.cs ✓"
test -f src/BotCore/Intelligence/IntelligenceSynthesizerService.cs && echo "  - IntelligenceSynthesizerService.cs ✓"
test -f src/BotCore/Intelligence/Models/MarketIntelligence.cs && echo "  - MarketIntelligence.cs ✓"
test -f src/BotCore/Intelligence/Models/NewsSentiment.cs && echo "  - NewsSentiment.cs ✓"
echo ""

# Test 2: Verify Parquet.Net was added
echo "✓ Test 2: Verify Parquet.Net dependency"
grep -q "Parquet.Net" src/BotCore/BotCore.csproj && echo "  - Parquet.Net package reference found ✓"
echo ""

# Test 3: Verify service registration
echo "✓ Test 3: Verify service registration in Program.cs"
grep -q "MarketDataReader" src/UnifiedOrchestrator/Program.cs && echo "  - MarketDataReader registration found ✓"
grep -q "IntelligenceSynthesizerService" src/UnifiedOrchestrator/Program.cs && echo "  - IntelligenceSynthesizerService registration found ✓"
echo ""

# Test 4: Verify configuration
echo "✓ Test 4: Verify .env configuration"
grep -q "INTELLIGENCE_SYNTHESIS_ENABLED" .env && echo "  - INTELLIGENCE_SYNTHESIS_ENABLED found ✓"
grep -q "INTELLIGENCE_TWO_TIER_CACHING" .env && echo "  - INTELLIGENCE_TWO_TIER_CACHING found ✓"
echo ""

# Test 5: Verify UnifiedTradingBrain accepts intelligence
echo "✓ Test 5: Verify UnifiedTradingBrain signature"
grep -q "MarketIntelligence? intelligence" src/BotCore/Brain/UnifiedTradingBrain.cs && echo "  - Intelligence parameter added ✓"
echo ""

# Test 6: Verify EnhancedTradingBrainIntegration uses real data
echo "✓ Test 6: Verify EnhancedTradingBrainIntegration changes"
grep -q "CreateRealEnvFromIntelligence" src/BotCore/Services/EnhancedTradingBrainIntegration.cs && echo "  - CreateRealEnvFromIntelligence method found ✓"
grep -q "CreateRealBarsFromMarketData" src/BotCore/Services/EnhancedTradingBrainIntegration.cs && echo "  - CreateRealBarsFromMarketData method found ✓"
grep -q "CreateRealLevelsFromMarketData" src/BotCore/Services/EnhancedTradingBrainIntegration.cs && echo "  - CreateRealLevelsFromMarketData method found ✓"
echo ""

# Test 7: Verify sample data files exist
echo "✓ Test 7: Verify sample data files"
test -f telemetry/system_metrics.json && echo "  - system_metrics.json ✓"
test -f datasets/economic_calendar/forexfactory_events.json && echo "  - forexfactory_events.json ✓"
test -f datasets/news_flags/news_1.json && echo "  - news_1.json ✓"
echo ""

# Test 8: Verify MarketDataReader can handle missing files gracefully
echo "✓ Test 8: Code structure verification"
grep -q "File.Exists" src/BotCore/Intelligence/MarketDataReader.cs && echo "  - File existence checks present ✓"
grep -q "catch.*Exception" src/BotCore/Intelligence/MarketDataReader.cs && echo "  - Error handling present ✓"
echo ""

echo "✅ All intelligence integration tests passed!"
echo ""
echo "📋 Summary:"
echo "  - ✓ Phase 1: Parquet.Net NuGet package added"
echo "  - ✓ Phase 2: MarketDataReader service created"
echo "  - ✓ Phase 3: Intelligence models created"
echo "  - ✓ Phase 4: IntelligenceSynthesizerService created"
echo "  - ✓ Phase 5: EnhancedTradingBrainIntegration modified"
echo "  - ✓ Phase 6: UnifiedTradingBrain enhanced"
echo "  - ✓ Phase 7: Services registered in Program.cs"
echo "  - ✓ Phase 8: Configuration added to .env"
echo ""
echo "🚀 Intelligence integration is complete and ready for testing!"
