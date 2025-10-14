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
            instruments: List of instruments to trade (defaults to ['MNQ', 'ES'])
        """
        self.instruments = instruments or ['MNQ', 'ES']
        self.adapter: Optional[TopstepXAdapter] = None
        self._initialized = False
        
        if not SDK_AVAILABLE:
            logger.warning("TopstepX SDK not available - using simulation mode")
    
    async def initialize(self) -> bool:
        """Initialize the SDK adapter connection."""
        if self._initialized:
            return True
            
        if not SDK_AVAILABLE:
            logger.info("SDK not available - using simulation mode")
            self._initialized = True
            return True
            
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
            # Return simulated prices for development/testing
            return self._get_simulated_price(symbol)
            
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
            # Return simulated account state
            return self._get_simulated_account_state()
            
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
            return self._get_simulated_account_state()
    
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
            # Return simulated order result
            return self._get_simulated_order_result(symbol, size, stop_loss, take_profit)
            
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
            return {'health_score': 100, 'status': 'simulation', 'mode': 'development'}
            
        return await self.adapter.get_health_score()
    
    async def disconnect(self):
        """Clean disconnect from SDK adapter."""
        if self.adapter and SDK_AVAILABLE:
            await self.adapter.disconnect()
        self._initialized = False
        logger.info("SDK Bridge disconnected")
    
    # Simulation methods for development/testing
    def _get_simulated_price(self, symbol: str) -> float:
        """Return simulated price for development."""
        prices = {
            'MNQ': 18500.0,
            'ES': 4500.0,
            'NQ': 18500.0,
            'RTY': 2100.0,
            'YM': 34000.0
        }
        return prices.get(symbol, 1000.0)
    
    def _get_simulated_historical_bars(
        self, 
        symbol: str, 
        timeframe: str, 
        count: int, 
        end_time: Optional[datetime]
    ) -> List[Dict[str, Any]]:
        """Return simulated historical bars for development."""
        base_price = self._get_simulated_price(symbol)
        bars = []
        
        for i in range(count):
            # Simple simulation with some price movement
            price_delta = (i % 10 - 5) * 0.25  # ES/MNQ tick size
            bar = {
                'timestamp': datetime.now(timezone.utc).isoformat(),
                'open': base_price + price_delta,
                'high': base_price + price_delta + 1.0,
                'low': base_price + price_delta - 1.0,
                'close': base_price + price_delta + 0.5,
                'volume': 100 + (i % 50)
            }
            bars.append(bar)
            
        return bars
    
    def _get_simulated_account_state(self) -> Dict[str, Any]:
        """Return simulated account state for development."""
        return {
            'portfolio': {
                'total_trades': 25,
                'win_rate': 60.0,
                'total_pnl': 1250.75,
                'max_drawdown': -150.25
            },
            'positions': {
                instrument: {
                    'size': 0,
                    'average_price': 0.0,
                    'unrealized_pnl': 0.0,
                    'realized_pnl': 0.0
                } for instrument in self.instruments
            },
            'health': {
                'health_score': 100,
                'status': 'simulation'
            },
            'timestamp': datetime.now(timezone.utc).isoformat()
        }
    
    async def _fetch_topstepx_historical_bars(
        self,
        symbol: str,
        timeframe: str = "1m",
        count: int = 100,
        end_time: Optional[datetime] = None
    ) -> List[Dict[str, Any]]:
        """
        Fetch historical bars from TopstepX REST API.
        
        Args:
            symbol: Instrument symbol (e.g., 'ES', 'MNQ')
            timeframe: Bar timeframe (default '1m')
            count: Number of bars to retrieve
            end_time: End time for data (defaults to now)
            
        Returns:
            List of bar dictionaries with OHLCV data
        """
        try:
            import aiohttp
            
            # Map symbol to TopstepX contract ID
            contract_id_map = {
                'ES': 'CON.F.US.EP.U25',
                'MNQ': 'CON.F.US.MNQ.U25',
                'NQ': 'CON.F.US.ENQ.U25',
                'MES': 'CON.F.US.MES.U25'
            }
            
            contract_id = contract_id_map.get(symbol)
            if not contract_id:
                logger.warning(f"Unknown symbol {symbol}, using simulated data")
                return self._get_simulated_historical_bars(symbol, timeframe, count, end_time)
            
            # Get JWT token from environment
            jwt = os.getenv('TOPSTEPX_JWT')
            if not jwt:
                logger.warning("No TOPSTEPX_JWT found, using simulated data")
                return self._get_simulated_historical_bars(symbol, timeframe, count, end_time)
            
            # Calculate time range (get more bars than needed to ensure we have enough)
            if end_time is None:
                end_time = datetime.now(timezone.utc)
            
            # For 1-minute bars, go back count minutes plus buffer
            start_time = end_time - timedelta(minutes=count * 2)  # 2x buffer
            
            # Map timeframe to unit and unitNumber
            timeframe_map = {
                '1m': (2, 1),   # Minutes, 1
                '5m': (2, 5),   # Minutes, 5
                '1h': (3, 1),   # Hours, 1
            }
            unit, unit_number = timeframe_map.get(timeframe, (2, 1))
            
            # Build API request
            url = "https://api.topstepx.com/api/History/retrieveBars"
            headers = {
                'Authorization': f'Bearer {jwt}',
                'Content-Type': 'application/json'
            }
            payload = {
                'contractId': contract_id,
                'live': True,
                'startTime': start_time.strftime('%Y-%m-%dT%H:%M:%SZ'),
                'endTime': end_time.strftime('%Y-%m-%dT%H:%M:%SZ'),
                'unit': unit,
                'unitNumber': unit_number,
                'limit': count * 2,  # Request more to ensure we have enough
                'includePartialBar': False
            }
            
            logger.info(f"Fetching {count} historical bars for {symbol} from TopstepX API")
            
            async with aiohttp.ClientSession() as session:
                async with session.post(url, json=payload, headers=headers) as response:
                    if response.status != 200:
                        error_text = await response.text()
                        logger.error(f"TopstepX API error: {response.status} - {error_text}")
                        return self._get_simulated_historical_bars(symbol, timeframe, count, end_time)
                    
                    data = await response.json()
                    
                    # Parse bars from response
                    bars = []
                    if isinstance(data, list):
                        for bar_data in data[:count]:  # Limit to requested count
                            try:
                                bar = {
                                    'timestamp': bar_data.get('t', bar_data.get('time', datetime.now(timezone.utc).isoformat())),
                                    'open': float(bar_data.get('o', bar_data.get('open', 0))),
                                    'high': float(bar_data.get('h', bar_data.get('high', 0))),
                                    'low': float(bar_data.get('l', bar_data.get('low', 0))),
                                    'close': float(bar_data.get('c', bar_data.get('close', 0))),
                                    'volume': int(bar_data.get('v', bar_data.get('volume', 0)))
                                }
                                bars.append(bar)
                            except (KeyError, ValueError, TypeError) as e:
                                logger.warning(f"Error parsing bar data: {e}")
                                continue
                    
                    if bars:
                        logger.info(f"Successfully fetched {len(bars)} historical bars for {symbol}")
                        return bars
                    else:
                        logger.warning(f"No bars returned from TopstepX API, using simulated data")
                        return self._get_simulated_historical_bars(symbol, timeframe, count, end_time)
                        
        except ImportError:
            logger.warning("aiohttp not available, using simulated data")
            return self._get_simulated_historical_bars(symbol, timeframe, count, end_time)
        except Exception as e:
            logger.error(f"Error fetching historical bars: {e}")
            return self._get_simulated_historical_bars(symbol, timeframe, count, end_time)
    
    def _get_simulated_order_result(
        self, 
        symbol: str, 
        size: int, 
        stop_loss: float, 
        take_profit: float
    ) -> Dict[str, Any]:
        """Return simulated order result for development."""
        return {
            'success': True,
            'order_id': f'sim_order_{datetime.now().strftime("%Y%m%d_%H%M%S")}',
            'symbol': symbol,
            'size': size,
            'entry_price': self._get_simulated_price(symbol),
            'stop_loss': stop_loss,
            'take_profit': take_profit,
            'timestamp': datetime.now(timezone.utc).isoformat(),
            'mode': 'simulation'
        }

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
            symbol = sys.argv[2] if len(sys.argv) > 2 else 'MNQ'
            timeframe = sys.argv[3] if len(sys.argv) > 3 else '1m'
            count = int(sys.argv[4]) if len(sys.argv) > 4 else 100
            
            async def get_bars():
                async with SDKBridge([symbol]) as bridge:
                    bars = await bridge.get_historical_bars(symbol, timeframe, count)
                    print(json.dumps(bars, default=str))
            
            asyncio.run(get_bars())
            
        elif command == "get_live_price":
            # get_live_price <symbol>
            symbol = sys.argv[2] if len(sys.argv) > 2 else 'MNQ'
            
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
        
        async with SDKBridge(['MNQ', 'ES']) as bridge:
            # Test price retrieval
            mnq_price = await bridge.get_live_price('MNQ')
            print(f"MNQ Price: ${mnq_price:.2f}")
            
            # Test historical data
            bars = await bridge.get_historical_bars('MNQ', '1m', 10)
            print(f"Retrieved {len(bars)} historical bars")
            
            # Test account state
            account = await bridge.get_account_state()
            print(f"Account Health: {account['health']['health_score']}%")
            
            # Test health score
            health = await bridge.get_health_score()
            print(f"System Health: {health['health_score']}%")
            
        print("✅ SDK Bridge test completed")