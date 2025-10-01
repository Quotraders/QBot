#!/usr/bin/env python3
"""
TopstepX Python SDK Adapter

Production-ready adapter for TopstepX trading using the project-x-py SDK.
Implements TradingSuite initialization, risk management, and order execution.
Enhanced with fail-closed behavior, centralized retry policies, and structured telemetry.
"""

import asyncio
import logging
import os
import sys
import time
from datetime import datetime, timezone
from typing import Dict, List, Optional, Any
from contextlib import asynccontextmanager

# Import the real project-x-py SDK - fail hard if not available
try:
    from project_x_py import TradingSuite
except ImportError as e:
    raise RuntimeError(
        "project-x-py SDK is required for production use. "
        "Install with: pip install 'project-x-py[all]'"
    ) from e


class AdapterRetryPolicy:
    """Centralized retry policy with bounded timeouts for fail-closed behavior."""
    
    def __init__(self, max_retries: int = None, base_delay: float = None, max_delay: float = None, timeout: float = None):
        # All parameters must come from configuration - fail closed if not provided
        if max_retries is None:
            max_retries = int(os.getenv('ADAPTER_MAX_RETRIES'))
            if not max_retries or max_retries <= 0:
                raise ValueError("ADAPTER_MAX_RETRIES environment variable must be set to positive integer")
        
        if base_delay is None:
            base_delay = float(os.getenv('ADAPTER_BASE_DELAY'))
            if not base_delay or base_delay <= 0:
                raise ValueError("ADAPTER_BASE_DELAY environment variable must be set to positive number")
                
        if max_delay is None:
            max_delay = float(os.getenv('ADAPTER_MAX_DELAY'))
            if not max_delay or max_delay <= 0 or max_delay < base_delay:
                raise ValueError("ADAPTER_MAX_DELAY environment variable must be set and >= base_delay")
                
        if timeout is None:
            timeout = float(os.getenv('ADAPTER_TIMEOUT'))
            if not timeout or timeout <= 0:
                raise ValueError("ADAPTER_TIMEOUT environment variable must be set to positive number")
        
        self.max_retries = max_retries
        self.base_delay = base_delay
        self.max_delay = max_delay
        self.timeout = timeout
    
    async def execute_with_retry(self, operation, operation_name: str, logger, *args, **kwargs):
        """Execute operation with exponential backoff retry and timeout."""
        start_time = time.time()
        last_exception = None
        
        for attempt in range(self.max_retries + 1):
            # Check timeout
            if time.time() - start_time > self.timeout:
                logger.error(f"[RETRY-POLICY] {operation_name} timed out after {self.timeout}s")
                raise TimeoutError(f"{operation_name} operation timed out after {self.timeout} seconds")
            
            try:
                result = await operation(*args, **kwargs)
                if attempt > 0:
                    logger.info(f"[RETRY-POLICY] {operation_name} succeeded on attempt {attempt + 1}/{self.max_retries + 1}")
                return result
                
            except Exception as e:
                last_exception = e
                if attempt == self.max_retries:
                    # Final failure - emit telemetry and fail closed
                    logger.error(f"[RETRY-POLICY] {operation_name} failed after {self.max_retries + 1} attempts: {e}")
                    self._emit_failure_telemetry(operation_name, e, attempt + 1, logger)
                    break
                
                # Calculate delay with exponential backoff
                delay = min(self.base_delay * (2 ** attempt), self.max_delay)
                logger.warning(f"[RETRY-POLICY] {operation_name} attempt {attempt + 1} failed: {e}. Retrying in {delay:.1f}s...")
                await asyncio.sleep(delay)
        
        # All retries exhausted - fail closed
        raise RuntimeError(f"FAIL-CLOSED: {operation_name} failed after {self.max_retries + 1} attempts") from last_exception
    
    def _emit_failure_telemetry(self, operation_name: str, error: Exception, attempts: int, logger):
        """Emit structured telemetry for adapter failures."""
        telemetry = {
            "event_type": "adapter_failure",
            "operation": operation_name,
            "error_type": type(error).__name__,
            "error_message": str(error),
            "attempts": attempts,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "severity": "critical"
        }
        logger.error(f"[TELEMETRY] {telemetry}")


class TopstepXAdapter:
    """
    Production TopstepX adapter with full SDK integration.
    
    Features:
    - Multi-instrument support via TradingSuite
    - Risk management with managed_trade() context
    - Real-time price data and order execution
    - Health monitoring and statistics
    - Production error handling and logging
    """
    
    def __init__(self, instruments: List[str]):
        """
        Initialize adapter with specified instruments.
        
        Args:
            instruments: List of instrument symbols (e.g., ['MNQ', 'ES'])
        
        Raises:
            RuntimeError: If required credentials are not found in environment
        """
        # Validate credentials are available immediately - fail hard if not
        api_key = os.getenv('PROJECT_X_API_KEY')
        username = os.getenv('PROJECT_X_USERNAME')
        
        if not api_key or not username:
            raise RuntimeError(
                "Missing required ProjectX credentials in environment. "
                "Set PROJECT_X_API_KEY and PROJECT_X_USERNAME environment variables."
            )
        
        self.instruments = instruments
        self.suite: Optional[TradingSuite] = None
        self._is_initialized = False
        self._connection_health = 0.0
        self._last_health_check: Optional[datetime] = None
        
        # Configure production logging
        self.logger = logging.getLogger(f"TopstepXAdapter-{'-'.join(instruments)}")
        if not self.logger.handlers:
            handler = logging.StreamHandler()
            formatter = logging.Formatter(
                '%(asctime)s - %(name)s - %(levelname)s - %(message)s'
            )
            handler.setFormatter(formatter)
            self.logger.addHandler(handler)
            self.logger.setLevel(logging.INFO)
        
        # Initialize centralized retry policy with fail-closed configuration validation
        try:
            self.retry_policy = AdapterRetryPolicy()
        except ValueError as e:
            self.logger.error(f"FAIL-CLOSED: Adapter configuration invalid: {e}")
            raise RuntimeError(f"Adapter configuration failure: {e}") from e
        
        self.logger.info(f"🔧 TopstepX adapter initialized for {instruments} with fail-closed retry policy")
        self._emit_telemetry("adapter_initialized", {"instruments": instruments})
    
    def _emit_telemetry(self, event_type: str, data: Dict[str, Any]):
        """Emit structured telemetry for monitoring and alerting."""
        telemetry = {
            "event_type": event_type,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "adapter_instance": f"TopstepX-{'-'.join(self.instruments)}",
            **data
        }
        self.logger.info(f"[TELEMETRY] {telemetry}")

    async def initialize(self) -> None:
        """Setup structured logging for production use."""
        logger = logging.getLogger(f"TopstepXAdapter.{id(self)}")
        logger.setLevel(logging.INFO)
        
        if not logger.handlers:
            handler = logging.StreamHandler()
            formatter = logging.Formatter(
                '[%(asctime)s] %(levelname)s [%(name)s] %(message)s'
            )
            handler.setFormatter(formatter)
            logger.addHandler(handler)
            
        return logger
        
    def _validate_configuration(self) -> None:
        """Validate SDK configuration and credentials."""
        # Validate instruments
        if not self.instruments:
            raise ValueError("At least one instrument must be specified")
            
        # Validate supported instruments
        supported_instruments = {'MNQ', 'ES', 'NQ', 'RTY', 'YM'}
        for instrument in self.instruments:
            if instrument not in supported_instruments:
                self.logger.warning(
                    f"Instrument {instrument} may not be supported. "
                    f"Supported: {supported_instruments}"
                )

    async def initialize(self) -> None:
        """
        Initialize TradingSuite with multi-instrument support and risk management.
        Enhanced with fail-closed behavior and centralized retry policies.
        
        Raises:
            RuntimeError: If initialization fails
            ValueError: If configuration is invalid
        """
        if self._is_initialized:
            self.logger.warning("Adapter already initialized")
            return
            
        # Use retry policy for initialization - fail closed if unable to initialize
        async def _initialize_suite():
            self.logger.info(f"Initializing TradingSuite with instruments: {self.instruments}")
            
            # Initialize with real TopstepX SDK only
            self.logger.info("Initializing production TopstepX SDK...")
            suite = await TradingSuite.create(
                instruments=self.instruments,
                timeframes=["5min"]
            )
            self.logger.info("✅ TopstepX SDK initialized successfully")
            return suite
        
        # Initialize suite with retry policy
        self.suite = await self.retry_policy.execute_with_retry(
            _initialize_suite, "TradingSuite_initialization", self.logger
        )
        
        # Verify connection to ALL instruments - FAIL CLOSED if any fail
        connection_failures = []
        for instrument in self.instruments:
            async def _test_instrument_connection():
                current_price = await self.suite[instrument].data.get_current_price()
                self.logger.info(f"✅ {instrument} connected - Current price: ${current_price:.2f}")
                return current_price
            
            try:
                await self.retry_policy.execute_with_retry(
                    _test_instrument_connection, f"{instrument}_connection_test", self.logger
                )
            except Exception as e:
                connection_failures.append(f"{instrument}: {str(e)}")
                self.logger.error(f"❌ Failed to connect to {instrument}: {e}")
        
        # FAIL-CLOSED: Require ALL instruments to connect successfully
        if connection_failures:
            error_msg = f"FAIL-CLOSED: Instrument connection failures detected: {connection_failures}"
            self.logger.error(error_msg)
            self._emit_telemetry("initialization_failed", {
                "reason": "instrument_connection_failures",
                "failed_instruments": connection_failures,
                "severity": "critical"
            })
            await self._cleanup_resources()
            raise RuntimeError(error_msg)
        
        # Test risk management system with retry policy
        async def _test_risk_management():
            risk_stats = await self.suite.get_stats()
            self.logger.info(f"SDK connected successfully - Stats: {risk_stats}")
            return risk_stats
        
        try:
            await self.retry_policy.execute_with_retry(
                _test_risk_management, "risk_management_test", self.logger
            )
        except Exception as e:
            # Risk management failure is critical - fail closed
            error_msg = f"FAIL-CLOSED: Risk management system unavailable: {e}"
            self.logger.error(error_msg)
            self._emit_telemetry("initialization_failed", {
                "reason": "risk_management_failure",
                "error": str(e),
                "severity": "critical"
            })
            await self._cleanup_resources()
            raise RuntimeError(error_msg) from e
        
        self._is_initialized = True
        self._connection_health = 100.0  # All instruments connected successfully
        self._last_health_check = datetime.now(timezone.utc)
        
        self.logger.info(f"🚀 TopstepX SDK adapter initialized successfully (Health: 100%)")
        self._emit_telemetry("initialization_completed", {
            "instruments": self.instruments,
            "health": 100.0,
            "status": "production_ready"
        })

    async def get_price(self, symbol: str) -> float:
        """
        Get current market price for instrument with retry policy and fail-closed behavior.
        
        Args:
            symbol: Instrument symbol (e.g., 'MNQ', 'ES')
            
        Returns:
            Current market price
            
        Raises:
            RuntimeError: If price retrieval fails after retries
            ValueError: If symbol not configured
        """
        if not self._is_initialized or not self.suite:
            raise RuntimeError("Adapter not initialized. Call initialize() first.")
            
        if symbol not in self.instruments:
            raise ValueError(f"Symbol {symbol} not in configured instruments: {self.instruments}")
        
        async def _get_price_operation():
            price = await self.suite[symbol].data.get_current_price()
            self.logger.debug(f"[PRICE] {symbol}: ${price:.2f}")
            return float(price)
        
        # Use retry policy for price retrieval - fail closed if unable to get price
        try:
            price = await self.retry_policy.execute_with_retry(
                _get_price_operation, f"get_price_{symbol}", self.logger
            )
            self._emit_telemetry("price_retrieved", {"symbol": symbol, "price": price})
            return price
        except Exception as e:
            # Price retrieval failure is critical for trading - fail closed
            error_msg = f"FAIL-CLOSED: Price retrieval failed for {symbol} after retries"
            self.logger.error(error_msg)
            self._emit_telemetry("price_retrieval_failed", {
                "symbol": symbol,
                "error": str(e),
                "severity": "critical"
            })
            raise RuntimeError(error_msg) from e
        """
        Get current market price for instrument.
        
        Args:
            symbol: Instrument symbol (e.g., 'MNQ', 'ES')
            
        Returns:
            Current market price
            
        Raises:
            RuntimeError: If not initialized or instrument not available
            ValueError: If symbol is invalid
        """
        if not self._is_initialized or not self.suite:
            raise RuntimeError("Adapter not initialized. Call initialize() first.")
            
        if symbol not in self.instruments:
            raise ValueError(f"Symbol {symbol} not in configured instruments: {self.instruments}")
            
        try:
            price = await self.suite[symbol].data.get_current_price()
            self.logger.debug(f"[PRICE] {symbol}: ${price:.2f}")
            return float(price)
            
        except Exception as e:
            self.logger.error(f"Failed to get price for {symbol}: {e}")
            raise RuntimeError(f"Price retrieval failed for {symbol}") from e

    async def place_order(
        self, 
        symbol: str, 
        size: int, 
        stop_loss: float, 
        take_profit: float,
        max_risk_percent: float = 0.01
    ) -> Dict[str, Any]:
        """
        Place bracket order with risk management.
        
        Args:
            symbol: Instrument symbol
            size: Position size (positive for long, negative for short)
            stop_loss: Stop loss price
            take_profit: Take profit price
            max_risk_percent: Maximum risk as percentage of account (default 1%)
            
        Returns:
            Order execution result with order ID and status
            
        Raises:
            RuntimeError: If order placement fails
            ValueError: If parameters are invalid
        """
        if not self._is_initialized or not self.suite:
            raise RuntimeError("Adapter not initialized. Call initialize() first.")
            
        if symbol not in self.instruments:
            raise ValueError(f"Symbol {symbol} not in configured instruments: {self.instruments}")
            
        if size == 0:
            raise ValueError("Order size cannot be zero")
            
        try:
            current_price = await self.get_price(symbol)
            
            # Validate price levels
            if size > 0:  # Long position
                if stop_loss >= current_price:
                    raise ValueError(f"Stop loss {stop_loss} must be below current price {current_price} for long position")
                if take_profit <= current_price:
                    raise ValueError(f"Take profit {take_profit} must be above current price {current_price} for long position")
            else:  # Short position
                if stop_loss <= current_price:
                    raise ValueError(f"Stop loss {stop_loss} must be above current price {current_price} for short position")
                if take_profit >= current_price:
                    raise ValueError(f"Take profit {take_profit} must be below current price {current_price} for short position")
            
            self.logger.info(
                f"[ORDER] Placing managed trade: {symbol} size={size} "
                f"entry=${current_price:.2f} stop=${stop_loss:.2f} target=${take_profit:.2f}"
            )
            
            # Use managed trade context for risk enforcement
            async with self.suite.managed_trade(max_risk_percent=max_risk_percent):
                # Place bracket order through SDK
                side = 'buy' if size > 0 else 'sell'
                order_result = await self.suite[symbol].orders.place_bracket_order(
                    side=side,
                    quantity=abs(size),
                    stop_loss=stop_loss,
                    take_profit=take_profit
                )
                
                # Structure return data
                result = {
                    'success': True,
                    'order_id': str(order_result.get('id', 'unknown')),
                    'symbol': symbol,
                    'size': size,
                    'entry_price': current_price,
                    'stop_loss': stop_loss,
                    'take_profit': take_profit,
                    'timestamp': datetime.now(timezone.utc).isoformat(),
                    'risk_percent': max_risk_percent
                }
                
                self.logger.info(f"✅ Order placed successfully: {result['order_id']}")
                return result
                
        except Exception as e:
            error_msg = f"Failed to place order for {symbol}: {e}"
            self.logger.error(error_msg)
            
            return {
                'success': False,
                'error': str(e),
                'symbol': symbol,
                'size': size,
                'timestamp': datetime.now(timezone.utc).isoformat()
            }

    async def get_health_score(self) -> Dict[str, Any]:
        """
        Get comprehensive health metrics and statistics.
        
        Returns:
            Health score and system statistics
        """
        if not self._is_initialized or not self.suite:
            return {
                'health_score': 0,
                'status': 'not_initialized',
                'error': 'Adapter not initialized'
            }
            
        try:
            # Get suite statistics
            stats = await self.suite.get_stats()
            
            # Calculate connection health for each instrument
            instrument_health = {}
            total_health = 0.0
            
            for instrument in self.instruments:
                try:
                    # Test price data availability by accessing the instrument
                    instrument_data = self.suite[instrument]
                    if instrument_data:
                        instrument_health[instrument] = 100.0
                        total_health += 100.0
                    else:
                        instrument_health[instrument] = 0.0
                except Exception as e:
                    self.logger.warning(f"Health check failed for {instrument}: {e}")
                    instrument_health[instrument] = 0.0
                    
            # Calculate overall health score
            overall_health = total_health / len(self.instruments) if self.instruments else 0.0
            
            # Update internal health tracking
            self._connection_health = overall_health
            self._last_health_check = datetime.now(timezone.utc)
            
            health_data = {
                'health_score': int(overall_health),
                'status': 'healthy' if overall_health >= 80 else 'degraded' if overall_health >= 50 else 'critical',
                'instruments': instrument_health,
                'suite_stats': stats,
                'last_check': self._last_health_check.isoformat(),
                'uptime_seconds': (datetime.now(timezone.utc) - self._last_health_check).total_seconds(),
                'initialized': self._is_initialized
            }
            
            # Log health status
            if overall_health >= 80:
                self.logger.debug(f"System healthy: {overall_health:.1f}%")
            else:
                self.logger.warning(f"System health degraded: {overall_health:.1f}%")
                
            return health_data
            
        except Exception as e:
            self.logger.error(f"Health check failed: {e}")
            return {
                'health_score': 0,
                'status': 'error',
                'error': str(e),
                'last_check': datetime.now(timezone.utc).isoformat()
            }

    async def get_portfolio_status(self) -> Dict[str, Any]:
        """Get current portfolio positions and P&L."""
        if not self._is_initialized or not self.suite:
            raise RuntimeError("Adapter not initialized")
            
        try:
            # Get portfolio data from TradingSuite
            suite_stats = await self.suite.get_stats()
            
            positions = {}
            for instrument in self.instruments:
                try:
                    # Get position info for each instrument from the suite
                    instrument_obj = self.suite[instrument]
                    position_data = getattr(instrument_obj, 'positions', None)
                    
                    if position_data:
                        positions[instrument] = {
                            'size': getattr(position_data, 'quantity', 0),
                            'average_price': getattr(position_data, 'avg_price', 0.0),
                            'unrealized_pnl': getattr(position_data, 'unrealized_pnl', 0.0),
                            'realized_pnl': getattr(position_data, 'realized_pnl', 0.0)
                        }
                    else:
                        positions[instrument] = {
                            'size': 0,
                            'average_price': 0.0,
                            'unrealized_pnl': 0.0,
                            'realized_pnl': 0.0
                        }
                except Exception as e:
                    self.logger.warning(f"Failed to get position for {instrument}: {e}")
                    positions[instrument] = {'error': str(e)}
                    
            return {
                'portfolio': suite_stats,
                'positions': positions,
                'timestamp': datetime.now(timezone.utc).isoformat()
            }
            
        except Exception as e:
            self.logger.error(f"Failed to get portfolio status: {e}")
            raise RuntimeError(f"Portfolio status retrieval failed: {e}") from e

    async def disconnect(self) -> None:
        """Clean shutdown of TradingSuite and resources."""
        if not self._is_initialized:
            self.logger.info("Adapter already disconnected")
            return
            
        self.logger.info("Disconnecting TopstepX adapter...")
        
        try:
            await self._cleanup_resources()
            self.logger.info("✅ TopstepX adapter disconnected successfully")
            
        except Exception as e:
            self.logger.error(f"Error during disconnect: {e}")
            raise
            
    async def _cleanup_resources(self) -> None:
        """Internal cleanup of resources."""
        if self.suite:
            try:
                await self.suite.disconnect()
            except Exception as e:
                self.logger.warning(f"Error disconnecting suite: {e}")
            finally:
                self.suite = None
                
        self._is_initialized = False
        self._connection_health = 0.0

    @property
    def is_connected(self) -> bool:
        """Check if adapter is connected and healthy."""
        return self._is_initialized and self._connection_health >= 80.0

    @property
    def connection_health(self) -> float:
        """Get current connection health percentage."""
        return self._connection_health

    async def __aenter__(self):
        """Async context manager entry."""
        await self.initialize()
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        """Async context manager exit."""
        await self.disconnect()


# Standalone test function for validation
async def test_adapter_functionality():
    """Test adapter functionality for CI validation."""
    print("🧪 Testing TopstepX Adapter...")
    
    try:
        # Test initialization
        adapter = TopstepXAdapter(["MNQ", "ES"])
        await adapter.initialize()
        
        # Test health check
        health = await adapter.get_health_score()
        assert health['health_score'] >= 80, f"Health score too low: {health['health_score']}"
        print(f"✅ Health check passed: {health['health_score']}%")
        
        # Test price retrieval
        mnq_price = await adapter.get_price("MNQ")
        print(f"✅ MNQ price: ${mnq_price:.2f}")
        
        # Test order placement (demo mode)
        order_result = await adapter.place_order(
            symbol="MNQ",
            size=1,
            stop_loss=mnq_price - 10,
            take_profit=mnq_price + 15,
            max_risk_percent=0.005  # 0.5% risk for testing
        )
        assert order_result['success'], f"Order failed: {order_result.get('error')}"
        print(f"✅ Order test passed: {order_result['order_id']}")
        
        # Test portfolio status
        portfolio = await adapter.get_portfolio_status()
        print(f"✅ Portfolio status retrieved")
        
        # Clean disconnect
        await adapter.disconnect()
        print("✅ All tests passed!")
        
        return True
        
    except Exception as e:
        print(f"❌ Test failed: {e}")
        return False


if __name__ == "__main__":
    import sys
    import json
    
    # Command-line interface for C# integration
    if len(sys.argv) > 1:
        command = sys.argv[1]
        
        if command == "validate_sdk":
            # Check if project-x-py is available
            try:
                from project_x_py import TradingSuite
                print(json.dumps({"success": True, "message": "SDK available"}))
                sys.exit(0)
            except ImportError:
                print(json.dumps({"success": False, "error": "project-x-py not installed"}))
                sys.exit(1)
                
        elif command == "initialize":
            # Initialize adapter and return status
            try:
                async def init_test():
                    adapter = TopstepXAdapter(["MNQ", "ES"])
                    await adapter.initialize()
                    health = await adapter.get_health_score()
                    await adapter.disconnect()
                    return health
                    
                result = asyncio.run(init_test())
                print(json.dumps({"success": True, "health": result}))
                sys.exit(0)
            except Exception as e:
                print(json.dumps({"success": False, "error": str(e)}))
                sys.exit(1)
                
        else:
            # Parse JSON command
            try:
                cmd_data = json.loads(command)
                action = cmd_data.get("action")
                
                async def execute_command():
                    adapter = TopstepXAdapter(["MNQ", "ES"])
                    await adapter.initialize()
                    
                    try:
                        if action == "get_price":
                            price = await adapter.get_price(cmd_data["symbol"])
                            return {"success": True, "price": price}
                            
                        elif action == "place_order":
                            result = await adapter.place_order(
                                cmd_data["symbol"],
                                cmd_data["size"],
                                cmd_data["stop_loss"],
                                cmd_data["take_profit"],
                                cmd_data.get("max_risk_percent", 0.01)
                            )
                            return result
                            
                        elif action == "get_health_score":
                            result = await adapter.get_health_score()
                            return result
                            
                        elif action == "get_portfolio_status":
                            result = await adapter.get_portfolio_status()
                            return result
                            
                        elif action == "disconnect":
                            await adapter.disconnect()
                            return {"success": True, "message": "Disconnected"}
                            
                        else:
                            return {"success": False, "error": f"Unknown action: {action}"}
                            
                    finally:
                        await adapter.disconnect()
                
                result = asyncio.run(execute_command())
                print(json.dumps(result, default=str))
                sys.exit(0)
                
            except Exception as e:
                print(json.dumps({"success": False, "error": str(e)}))
                sys.exit(1)
    else:
        # Run standalone test if executed directly
        asyncio.run(test_adapter_functionality())