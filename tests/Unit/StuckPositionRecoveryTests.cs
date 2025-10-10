using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BotCore.Configuration;
using BotCore.Models;
using BotCore.Services;

namespace TradingBot.Tests.Unit
{
    /// <summary>
    /// Tests for stuck position recovery system
    /// Validates configuration, classification logic, and basic recovery flow
    /// </summary>
    public class StuckPositionRecoveryTests
    {
        [Fact]
        public void Configuration_ValidDefaults_PassesValidation()
        {
            // Arrange
            var config = new StuckPositionRecoveryConfiguration();
            
            // Act & Assert - should not throw
            config.Validate();
            
            // Verify defaults
            Assert.Equal(60, config.ReconciliationIntervalSeconds);
            Assert.Equal(30, config.MonitorCheckIntervalSeconds);
            Assert.Equal(-500m, config.RunawayLossThresholdUsd);
            Assert.True(config.Enabled);
        }
        
        [Fact]
        public void Configuration_InvalidValues_ThrowsException()
        {
            // Arrange
            var config = new StuckPositionRecoveryConfiguration
            {
                ReconciliationIntervalSeconds = -1 // Invalid
            };
            
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }
        
        [Fact]
        public void Configuration_PositiveRunawayLoss_ThrowsException()
        {
            // Arrange
            var config = new StuckPositionRecoveryConfiguration
            {
                RunawayLossThresholdUsd = 500m // Should be negative
            };
            
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }
        
        [Fact]
        public void StuckPositionAlert_CreatesValidInstance()
        {
            // Arrange & Act
            var alert = new StuckPositionAlert
            {
                PositionId = "test-pos-1",
                Symbol = "ES",
                Quantity = 2,
                EntryPrice = 5000m,
                IsLong = true,
                EntryTimestamp = DateTime.UtcNow.AddMinutes(-10),
                CurrentPrice = 5010m,
                UnrealizedPnL = 20m,
                Classification = StuckPositionClassification.StuckExit,
                DetectionTimestamp = DateTime.UtcNow,
                Reason = "Exit order failed more than 5 minutes ago"
            };
            
            // Assert
            Assert.Equal("ES", alert.Symbol);
            Assert.Equal(2, alert.Quantity);
            Assert.True(alert.IsLong);
            Assert.Equal(StuckPositionClassification.StuckExit, alert.Classification);
        }
        
        [Fact]
        public void RecoveryLevel_EscalationOrder_IsCorrect()
        {
            // Verify the escalation order is defined correctly
            Assert.Equal(0, (int)RecoveryLevel.None);
            Assert.Equal(1, (int)RecoveryLevel.SmartRetry);
            Assert.Equal(2, (int)RecoveryLevel.FreshStart);
            Assert.Equal(3, (int)RecoveryLevel.MarketOrder);
            Assert.Equal(4, (int)RecoveryLevel.HumanEscalation);
            Assert.Equal(5, (int)RecoveryLevel.SystemShutdown);
        }
        
        [Fact]
        public void PositionRecoveryState_InitialState_IsValid()
        {
            // Arrange
            var alert = new StuckPositionAlert
            {
                Symbol = "NQ",
                Quantity = 1,
                EntryPrice = 18000m
            };
            
            // Act
            var state = new PositionRecoveryState
            {
                PositionId = "test-1",
                Alert = alert,
                CurrentLevel = RecoveryLevel.SmartRetry,
                RecoveryStartTime = DateTime.UtcNow,
                LastEscalationTime = DateTime.UtcNow
            };
            
            // Assert
            Assert.Equal("test-1", state.PositionId);
            Assert.Equal(RecoveryLevel.SmartRetry, state.CurrentLevel);
            Assert.False(state.IsResolved);
            Assert.Null(state.ResolvedTime);
            Assert.Empty(state.Actions);
        }
        
        [Fact]
        public void PositionDiscrepancy_BrokerOnly_CreatesCorrectly()
        {
            // Arrange & Act
            var discrepancy = new PositionDiscrepancy
            {
                Symbol = "ES",
                DiscrepancyType = "BrokerOnly",
                BrokerQuantity = 2,
                BrokerAvgPrice = 5000m,
                BotQuantity = null,
                BotAvgPrice = null,
                Resolution = "HandedOffToEmergencyExit"
            };
            
            // Assert
            Assert.Equal("ES", discrepancy.Symbol);
            Assert.Equal("BrokerOnly", discrepancy.DiscrepancyType);
            Assert.Equal(2, discrepancy.BrokerQuantity);
            Assert.Null(discrepancy.BotQuantity);
        }
        
        [Fact]
        public void PositionDiscrepancy_QuantityMismatch_CreatesCorrectly()
        {
            // Arrange & Act
            var discrepancy = new PositionDiscrepancy
            {
                Symbol = "NQ",
                DiscrepancyType = "QuantityMismatch",
                BrokerQuantity = 2,
                BrokerAvgPrice = 18000m,
                BotQuantity = 1,
                BotAvgPrice = 18100m,
                Resolution = "UpdatedBotToMatchBroker"
            };
            
            // Assert
            Assert.Equal("NQ", discrepancy.Symbol);
            Assert.Equal("QuantityMismatch", discrepancy.DiscrepancyType);
            Assert.Equal(2, discrepancy.BrokerQuantity);
            Assert.Equal(1, discrepancy.BotQuantity);
        }
        
        [Fact]
        public void RecoveryIncident_TrackingMetrics_IsComplete()
        {
            // Arrange
            var incident = new RecoveryIncident
            {
                PositionId = "test-pos-1",
                Symbol = "ES",
                EntryPrice = 5000m,
                Quantity = 2,
                DetectionTimestamp = DateTime.UtcNow.AddMinutes(-2),
                Classification = StuckPositionClassification.RunawayLoss,
                FinalOutcome = "Resolved",
                TotalRecoveryTimeSeconds = 65.5,
                SlippageCost = -25m,
                MaxLevelReached = RecoveryLevel.MarketOrder,
                RequiredHumanIntervention = false
            };
            
            // Assert
            Assert.Equal("ES", incident.Symbol);
            Assert.Equal(StuckPositionClassification.RunawayLoss, incident.Classification);
            Assert.Equal("Resolved", incident.FinalOutcome);
            Assert.Equal(RecoveryLevel.MarketOrder, incident.MaxLevelReached);
            Assert.False(incident.RequiredHumanIntervention);
            Assert.Equal(-25m, incident.SlippageCost);
        }
        
        [Fact]
        public void ReconciliationResult_TracksDiscrepancies()
        {
            // Arrange
            var result = new PositionReconciliationResult
            {
                Timestamp = DateTime.UtcNow,
                BrokerPositionCount = 3,
                BotPositionCount = 2,
                DiscrepancyCount = 1
            };
            
            var discrepancy = new PositionDiscrepancy
            {
                Symbol = "ES",
                DiscrepancyType = "BrokerOnly",
                BrokerQuantity = 1,
                Resolution = "HandedOffToEmergencyExit"
            };
            
            result.Discrepancies.Add(discrepancy);
            result.ActionsTaken.Add("Ghost position ES: Initiated emergency exit");
            
            // Assert
            Assert.Equal(3, result.BrokerPositionCount);
            Assert.Equal(2, result.BotPositionCount);
            Assert.Equal(1, result.DiscrepancyCount);
            Assert.Single(result.Discrepancies);
            Assert.Single(result.ActionsTaken);
        }
        
        [Fact]
        public void BrokerPosition_ValidData_CreatesCorrectly()
        {
            // Arrange & Act
            var brokerPos = new BrokerPosition
            {
                Symbol = "MNQ",
                Quantity = -2, // Short
                AveragePrice = 18500m,
                UnrealizedPnL = 50m,
                LastUpdate = DateTime.UtcNow
            };
            
            // Assert
            Assert.Equal("MNQ", brokerPos.Symbol);
            Assert.Equal(-2, brokerPos.Quantity);
            Assert.Equal(18500m, brokerPos.AveragePrice);
            Assert.Equal(50m, brokerPos.UnrealizedPnL);
        }
        
        [Fact]
        public void ExitAttempt_TracksOrderDetails()
        {
            // Arrange & Act
            var attempt = new ExitAttempt
            {
                Timestamp = DateTime.UtcNow,
                OrderId = "order-123",
                OrderType = "Limit",
                Price = 5010m,
                Status = "Rejected",
                FailureReason = "Insufficient liquidity"
            };
            
            // Assert
            Assert.Equal("order-123", attempt.OrderId);
            Assert.Equal("Limit", attempt.OrderType);
            Assert.Equal(5010m, attempt.Price);
            Assert.Equal("Rejected", attempt.Status);
            Assert.Equal("Insufficient liquidity", attempt.FailureReason);
        }
        
        [Fact]
        public void RecoveryAction_TracksEscalationDetails()
        {
            // Arrange & Act
            var action = new RecoveryAction
            {
                Timestamp = DateTime.UtcNow,
                Level = RecoveryLevel.MarketOrder,
                ActionType = "MarketOrder",
                OrderId = "market-order-456",
                Price = 5005m,
                Result = "Success",
                Notes = "Market order filled immediately"
            };
            
            // Assert
            Assert.Equal(RecoveryLevel.MarketOrder, action.Level);
            Assert.Equal("MarketOrder", action.ActionType);
            Assert.Equal("market-order-456", action.OrderId);
            Assert.Equal(5005m, action.Price);
            Assert.Equal("Success", action.Result);
        }
    }
}
