#!/usr/bin/env python3
"""
SDK Bridge for Python ML/RL Modules

This module provides a bridge to the TopstepX SDK adapter for Python ML/RL/Cloud modules
that need to access live market data, account state, and historical data.
"""

import asyncio
import json
import subprocess
import sys
import os
from typing import Dict, List, Optional, Any, Union
from datetime import datetime, timezone, timedelta
import logging

# Try to import the TopstepX adapter, but gracefully handle missing SDK
try:
    # Add the adapters directory to the path
    ADAPTERS_PATH = os.path.join(os.path.dirname(__file__), '..', 'src', 'adapters')
    sys.path.insert(0, ADAPTERS_PATH)
    
    # Check if the real SDK is available first
    try:
        import project_x_py
        SDK_LIBRARY_AVAILABLE = True
    except ImportError:
        SDK_LIBRARY_AVAILABLE = False
    
    if SDK_LIBRARY_AVAILABLE:
        from topstep_x_adapter import TopstepXAdapter
        SDK_AVAILABLE = True
    else:
        TopstepXAdapter = None
        SDK_AVAILABLE = False
        
except ImportError:
    TopstepXAdapter = None
    SDK_AVAILABLE = False

logger = logging.getLogger(__name__)

class SDKBridge:
    """
    Bridge to TopstepX SDK adapter for Python ML/RL/Cloud modules.
    
    Provides:
    - Live market data access
    - Account state retrieval
    - Historical data fetching via adapter.get_historical_bars()
    - Order placement with risk management
    """
    
    def __init__(self, instruments: List[str] = None):
        """
        Initialize SDK bridge.
        
        Args:
            instruments: List of instruments to trade (defaults to ['ES', 'NQ'])
        """
        self.instruments = instruments or ['ES', 'NQ']
        self.adapter: Optional[TopstepXAdapter] = None
        self._initialized = False
        
        if not SDK_AVAILABLE:
            logger.warning("TopstepX SDK not available - using simulation mode")
    
    async def initialize(self) -> bool:
        """Initialize the SDK adapter connection."""
        if self._initialized:
            return True
            
        if not SDK_AVAILABLE:
            logger.error("PRODUCTION ERROR: TopstepX SDK (project-x-py) is NOT installed!")
            logger.error("Install with: pip install project-x-py")
            raise RuntimeError("TopstepX SDK not available - cannot operate in production mode")
            
        try:
            self.adapter = TopstepXAdapter(self.instruments)
            await self.adapter.initialize()
            self._initialized = True
            logger.info(f"SDK Bridge initialized with instruments: {self.instruments}")
            return True
        except Exception as e:
            logger.error(f"Failed to initialize SDK bridge: {e}")
            return False
    
    async def get_live_price(self, symbol: str) -> float:
        """
        Get live market price for symbol.
        
        Args:
            symbol: Instrument symbol (e.g., 'MNQ', 'ES')
            
        Returns:
            Current market price
            
        Raises:
            RuntimeError: If adapter not initialized or symbol unavailable
        """
        if not self._initialized:
            raise RuntimeError("SDK Bridge not initialized. Call initialize() first.")
            
        if not SDK_AVAILABLE or not self.adapter:
            raise RuntimeError("TopstepX SDK not available - cannot get live prices")
            
        return await self.adapter.get_price(symbol)
    
    async def get_historical_bars(
        self, 
        symbol: str, 
        timeframe: str = "1m",
        count: int = 100,
        end_time: Optional[datetime] = None
    ) -> List[Dict[str, Any]]:
        """
        Get historical bars via TopstepX REST API.
        
        This fetches real OHLCV historical data from TopstepX History API.
        
        Args:
            symbol: Instrument symbol (e.g., 'ES', 'MNQ')
            timeframe: Bar timeframe (e.g., '1m', '5m', '1h')
            count: Number of bars to retrieve
            end_time: End time for historical data (defaults to now)
            
        Returns:
            List of historical bars with OHLCV data
        """
        if not self._initialized:
            raise RuntimeError("SDK Bridge not initialized. Call initialize() first.")
            
        # Use real TopstepX API for historical data
        return await self._fetch_topstepx_historical_bars(symbol, timeframe, count, end_time)
    
    async def get_account_state(self) -> Dict[str, Any]:
        """
        Get current account state and positions.
        
        Returns:
            Account state including balance, positions, P&L
        """
        if not self._initialized:
            raise RuntimeError("SDK Bridge not initialized. Call initialize() first.")
            
        if not SDK_AVAILABLE or not self.adapter:
            raise RuntimeError("TopstepX SDK not available - cannot get account state")
            
        try:
            portfolio = await self.adapter.get_portfolio_status()
            health = await self.adapter.get_health_score()
            
            return {
                'portfolio': portfolio,
                'health': health,
                'timestamp': datetime.now(timezone.utc).isoformat()
            }
        except Exception as e:
            logger.error(f"Failed to get account state: {e}")
            raise
    
    async def place_order(
        self,
        symbol: str,
        size: int,
        stop_loss: float,
        take_profit: float,
        max_risk_percent: float = 0.01
    ) -> Dict[str, Any]:
        """
        Place order via SDK adapter with risk management.
        
        Args:
            symbol: Instrument symbol
            size: Position size (positive for long, negative for short)
            stop_loss: Stop loss price
            take_profit: Take profit price
            max_risk_percent: Maximum risk as percentage of account
            
        Returns:
            Order execution result
        """
        if not self._initialized:
            raise RuntimeError("SDK Bridge not initialized. Call initialize() first.")
            
        if not SDK_AVAILABLE or not self.adapter:
            raise RuntimeError("TopstepX SDK not available - cannot place orders")
            
        return await self.adapter.place_order(
            symbol=symbol,
            size=size,
            stop_loss=stop_loss,
            take_profit=take_profit,
            max_risk_percent=max_risk_percent
        )
    
    async def get_health_score(self) -> Dict[str, Any]:
        """Get system health metrics."""
        if not self._initialized:
            return {'health_score': 0, 'status': 'not_initialized'}
            
        if not SDK_AVAILABLE or not self.adapter:
            raise RuntimeError("TopstepX SDK not available - cannot get health score")
            
        return await self.adapter.get_health_score()
    
    async def disconnect(self):
        """Clean disconnect from SDK adapter."""
        if self.adapter and SDK_AVAILABLE:
            await self.adapter.disconnect()
        self._initialized = False
        logger.info("SDK Bridge disconnected")
    
    async def _fetch_topstepx_historical_bars(
        self,
        symbol: str,
        timeframe: str = "1m",
        count: int = 100,
        end_time: Optional[datetime] = None
    ) -> List[Dict[str, Any]]:
        """
        Fetch historical bars using project-x-py SDK client.get_bars() method.
        
        Args:
            symbol: Instrument symbol (e.g., 'ES', 'NQ')
            timeframe: Bar timeframe (default '1m')
            count: Number of bars to retrieve
            end_time: End time for data (defaults to now)
            
        Returns:
            List of bar dictionaries with OHLCV data
        """
        try:
            if not SDK_AVAILABLE or not self.adapter:
                logger.error("PRODUCTION ERROR: TopstepX SDK not available - cannot fetch real historical data")
                raise RuntimeError("TopstepX SDK not available - historical data requires real SDK connection")
            
            # Calculate days needed based on bars and timeframe
            # 1m bars: 100 bars = ~1-2 hours, use 1 day for safety
            # 5m bars: 100 bars = ~8 hours, use 1 day
            # 1h bars: 100 bars = ~4 days, use 5 days
            timeframe_to_days = {
                '1m': max(1, count // 720),   # ~720 1m bars per trading day (12 hours)
                '5m': max(1, count // 144),   # ~144 5m bars per trading day
                '15m': max(1, count // 48),   # ~48 15m bars per trading day
                '1h': max(2, count // 12)     # ~12 1h bars per trading day
            }
            days = timeframe_to_days.get(timeframe, 1)
            
            logger.info(f"Fetching {count} historical bars for {symbol} ({days} days)")
            
            # Use SDK's get_bars method via TradingSuite client
            # The SDK returns a Polars DataFrame which we need to convert to dict
            bars_data = await self.adapter.suite.client.get_bars(symbol, days=days)
            
            # Convert Polars DataFrame to list of dictionaries
            bars = []
            if bars_data is not None:
                # Polars DataFrame has to_dicts() method
                bars_dict_list = bars_data.to_dicts() if hasattr(bars_data, 'to_dicts') else bars_data.to_dict('records')
                
                for bar_data in bars_dict_list[:count]:  # Limit to requested count
                    try:
                        bar = {
                            'timestamp': bar_data.get('timestamp', bar_data.get('time', datetime.now(timezone.utc).isoformat())),
                            'open': float(bar_data.get('open', 0)),
                            'high': float(bar_data.get('high', 0)),
                            'low': float(bar_data.get('low', 0)),
                            'close': float(bar_data.get('close', 0)),
                            'volume': int(bar_data.get('volume', 0))
                        }
                        bars.append(bar)
                    except (KeyError, ValueError, TypeError) as e:
                        logger.warning(f"Error parsing bar data: {e}")
                        continue
            
            if bars:
                logger.info(f"Successfully fetched {len(bars)} historical bars for {symbol}")
                return bars
            else:
                logger.error(f"No bars returned from SDK for {symbol}")
                raise RuntimeError(f"No historical bars available from TopstepX SDK for {symbol}")
                        
        except Exception as e:
            logger.error(f"Error fetching historical bars via SDK: {e}")
            raise

    async def __aenter__(self):
        """Async context manager entry."""
        await self.initialize()
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        """Async context manager exit."""
        await self.disconnect()


# Global SDK bridge instance for easy import
sdk_bridge = SDKBridge()

# Convenience functions for quick access
async def get_live_price(symbol: str) -> float:
    """Get live price via global SDK bridge."""
    if not sdk_bridge._initialized:
        await sdk_bridge.initialize()
    return await sdk_bridge.get_live_price(symbol)

async def get_historical_bars(
    symbol: str, 
    timeframe: str = "1m", 
    count: int = 100
) -> List[Dict[str, Any]]:
    """Get historical bars via global SDK bridge."""
    if not sdk_bridge._initialized:
        await sdk_bridge.initialize()
    return await sdk_bridge.get_historical_bars(symbol, timeframe, count)

async def get_account_state() -> Dict[str, Any]:
    """Get account state via global SDK bridge."""
    if not sdk_bridge._initialized:
        await sdk_bridge.initialize()
    return await sdk_bridge.get_account_state()

# Example usage and testing
if __name__ == "__main__":
    import sys
    
    # Command-line interface for C# integration
    if len(sys.argv) > 1:
        command = sys.argv[1]
        
        if command == "get_historical_bars":
            # get_historical_bars <symbol> <timeframe> <count>
            symbol = sys.argv[2] if len(sys.argv) > 2 else 'NQ'
            timeframe = sys.argv[3] if len(sys.argv) > 3 else '1m'
            count = int(sys.argv[4]) if len(sys.argv) > 4 else 100
            
            async def get_bars():
                async with SDKBridge([symbol]) as bridge:
                    bars = await bridge.get_historical_bars(symbol, timeframe, count)
                    print(json.dumps(bars, default=str))
            
            asyncio.run(get_bars())
            
        elif command == "get_live_price":
            # get_live_price <symbol>
            symbol = sys.argv[2] if len(sys.argv) > 2 else 'NQ'
            
            async def get_price():
                async with SDKBridge([symbol]) as bridge:
                    price = await bridge.get_live_price(symbol)
                    print(json.dumps({"symbol": symbol, "price": price}))
            
            asyncio.run(get_price())
            
        elif command == "get_account_state":
            # get_account_state
            async def get_account():
                async with SDKBridge() as bridge:
                    account = await bridge.get_account_state()
                    print(json.dumps(account, default=str))
            
            asyncio.run(get_account())
            
        elif command == "test":
            # Run test suite
            asyncio.run(test_sdk_bridge())
            
        else:
            print(f"Unknown command: {command}")
            print("Available commands: get_historical_bars, get_live_price, get_account_state, test")
            sys.exit(1)
    else:
        # Run standalone test if executed directly
        asyncio.run(test_sdk_bridge())
        
    async def test_sdk_bridge():
        """Test SDK bridge functionality."""
        print("🧪 Testing SDK Bridge...")
        
        async with SDKBridge(['ES', 'NQ']) as bridge:
            # Test price retrieval
            nq_price = await bridge.get_live_price('NQ')
            print(f"NQ Price: ${nq_price:.2f}")
            
            # Test historical data
            bars = await bridge.get_historical_bars('NQ', '1m', 10)
            print(f"Retrieved {len(bars)} historical bars")
            
            # Test account state
            account = await bridge.get_account_state()
            print(f"Account Health: {account['health']['health_score']}%")
            
            # Test health score
            health = await bridge.get_health_score()
            print(f"System Health: {health['health_score']}%")
            
        print("✅ SDK Bridge test completed")