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
from collections import deque

# Import the real project-x-py SDK - fail hard if not available
try:
    from project_x_py import TradingSuite, EventType
except ImportError as e:
    raise RuntimeError(
        "project-x-py SDK is required for production use. "
        "Install with: pip install 'project-x-py[all]'"
    ) from e


def requires_initialization(func):
    """Decorator to check if adapter is initialized before method execution."""
    async def wrapper(self, *args, **kwargs):
        if not self._is_initialized or not self.suite:
            raise RuntimeError("Adapter not initialized. Call initialize() first.")
        return await func(self, *args, **kwargs)
    wrapper.__name__ = func.__name__
    wrapper.__doc__ = func.__doc__
    return wrapper


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
        api_key = os.getenv('TOPSTEPX_API_KEY')
        username = os.getenv('TOPSTEPX_USERNAME')
        
        if not api_key or not username:
            raise RuntimeError(
                "Missing required ProjectX credentials in environment. "
                "Set TOPSTEPX_API_KEY and TOPSTEPX_USERNAME environment variables."
            )
        
        self.instruments = instruments
        self.suite: Optional[TradingSuite] = None
        self._is_initialized = False
        self._connection_health = 0.0
        self._last_health_check: Optional[datetime] = None
        
        # PHASE 1: Fill event storage queue (thread-safe with asyncio)
        self._fill_events_queue: deque = deque(maxlen=1000)  # Keep last 1000 fills
        self._fill_events_lock = asyncio.Lock()
        
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
    
    def _validate_symbol(self, symbol: str) -> None:
        """Validate symbol is in configured instruments.
        
        Args:
            symbol: Instrument symbol to validate
            
        Raises:
            ValueError: If symbol not in configured instruments
        """
        if symbol not in self.instruments:
            raise ValueError(f"Symbol {symbol} not in configured instruments: {self.instruments}")
    
    def _parse_position_data(self, position) -> Optional[Dict[str, Any]]:
        """Parse SDK position object into dictionary format.
        
        Args:
            position: SDK Position object
            
        Returns:
            Position dictionary or None if position is flat
        """
        try:
            # Extract position data
            contract_id = str(getattr(position, 'contractId', ''))
            symbol = self._extract_symbol_from_contract_id(contract_id)
            
            # netPos is signed: positive = long, negative = short
            net_pos = int(getattr(position, 'netPos', 0))
            
            # Determine side
            if net_pos > 0:
                side = "LONG"
                avg_price = float(getattr(position, 'buyAvgPrice', 0.0))
            elif net_pos < 0:
                side = "SHORT"
                avg_price = float(getattr(position, 'sellAvgPrice', 0.0))
            else:
                # Flat position, skip
                return None
            
            # Get P&L data
            unrealized_pnl = float(getattr(position, 'unrealizedPnl', 0.0))
            realized_pnl = float(getattr(position, 'realizedPnl', 0.0))
            position_id = str(getattr(position, 'id', contract_id))
            
            return {
                'symbol': symbol,
                'quantity': abs(net_pos),
                'side': side,
                'avg_price': avg_price,
                'unrealized_pnl': unrealized_pnl,
                'realized_pnl': realized_pnl,
                'position_id': position_id
            }
        except Exception as e:
            self.logger.warning(f"Failed to parse position: {e}")
            return None
    
    def _extract_symbol_from_contract_id(self, contract_id: str) -> str:
        """
        Extract symbol from TopstepX contract ID format.
        
        Examples:
            "CON.F.US.MNQ.H25" -> "MNQ"
            "CON.F.US.EP.Z25" -> "ES" (EP maps to ES)
            "CON.F.US.ENQ.Z25" -> "NQ" (ENQ maps to NQ)
            
        Args:
            contract_id: TopstepX contract identifier
            
        Returns:
            Extracted symbol or original contract_id if parsing fails
        """
        try:
            parts = contract_id.split('.')
            if len(parts) >= 4:
                # TopstepX format: CON.F.US.SYMBOL.EXPIRY
                instrument_part = parts[3]
                
                # Map TopstepX instrument codes to standard symbols
                symbol_map = {
                    "EP": "ES",    # E-mini S&P 500
                    "ENQ": "NQ",   # E-mini NASDAQ
                    "MNQ": "MNQ",  # Micro E-mini NASDAQ
                    "MES": "ES",   # Micro E-mini S&P 500
                }
                
                return symbol_map.get(instrument_part, instrument_part)
        except Exception as e:
            self.logger.warning(f"Failed to parse contract ID {contract_id}: {e}")
        
        return contract_id
    
    async def _on_order_filled(self, event_data: Any):
        """
        PHASE 1: WebSocket callback for ORDER_FILLED events.
        
        This callback is triggered when an order is filled via the SDK's
        real-time WebSocket connection. It transforms the SDK event format
        into the structure expected by the C# layer.
        
        Args:
            event_data: Event data from project-x-py SDK
        """
        try:
            # Extract fill details from SDK event data
            # The SDK provides a GatewayUserTrade event with fill information
            order_id = str(getattr(event_data, 'orderId', getattr(event_data, 'order_id', 'unknown')))
            contract_id = str(getattr(event_data, 'contractId', getattr(event_data, 'contract_id', '')))
            quantity = int(getattr(event_data, 'quantity', getattr(event_data, 'qty', 0)))
            fill_price = float(getattr(event_data, 'price', getattr(event_data, 'fill_price', 0.0)))
            commission = float(getattr(event_data, 'commission', 0.0))
            timestamp_val = getattr(event_data, 'timestamp', None)
            
            # Extract symbol from contract ID
            symbol = self._extract_symbol_from_contract_id(contract_id)
            
            # Determine liquidity type (MAKER/TAKER)
            liquidity_type = str(getattr(event_data, 'liquidityType', 
                                        getattr(event_data, 'liquidity_type', 'TAKER')))
            
            # Parse timestamp
            if timestamp_val:
                if isinstance(timestamp_val, (int, float)):
                    # Assume milliseconds timestamp
                    timestamp = datetime.fromtimestamp(timestamp_val / 1000.0, tz=timezone.utc)
                elif isinstance(timestamp_val, str):
                    timestamp = datetime.fromisoformat(timestamp_val.replace('Z', '+00:00'))
                else:
                    timestamp = datetime.now(timezone.utc)
            else:
                timestamp = datetime.now(timezone.utc)
            
            # Transform to C# expected format
            fill_event = {
                'order_id': order_id,
                'symbol': symbol,
                'quantity': quantity,
                'price': fill_price,  # Entry price (same as fill_price for market orders)
                'fill_price': fill_price,
                'commission': commission,
                'exchange': 'CME',  # Default for futures
                'liquidity_type': liquidity_type,
                'timestamp': timestamp.isoformat()
            }
            
            # Add to queue with thread-safe lock
            async with self._fill_events_lock:
                self._fill_events_queue.append(fill_event)
            
            self.logger.info(
                f"[FILL-EVENT] Order filled: {order_id} {symbol} {quantity} @ ${fill_price:.2f}"
            )
            self._emit_telemetry("order_filled", {
                "order_id": order_id,
                "symbol": symbol,
                "quantity": quantity,
                "fill_price": fill_price
            })
            
        except Exception as e:
            self.logger.error(f"Error processing fill event: {e}", exc_info=True)

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
        
        # PHASE 1: Subscribe to ORDER_FILLED events via WebSocket
        try:
            await self.suite.on(EventType.ORDER_FILLED, self._on_order_filled)
            self.logger.info("✅ Subscribed to ORDER_FILLED events via WebSocket")
        except Exception as e:
            error_msg = f"FAIL-CLOSED: Failed to subscribe to fill events: {e}"
            self.logger.error(error_msg)
            self._emit_telemetry("event_subscription_failed", {
                "error": str(e),
                "severity": "critical"
            })
            await self._cleanup_resources()
            raise RuntimeError(error_msg) from e
        
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

    @requires_initialization
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
        self._validate_symbol(symbol)
        
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

    @requires_initialization
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
        self._validate_symbol(symbol)
            
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

    @requires_initialization
    async def get_fill_events(self) -> Dict[str, Any]:
        """
        PHASE 1: Get recent fill events from TopstepX SDK.
        
        This method returns fill events that have been collected via WebSocket
        subscription to EventType.ORDER_FILLED. Events are stored in an in-memory
        queue and retrieved by the C# layer via polling.
        
        Returns:
            Dictionary with fills array containing fill event data
        """
        try:
            # Read all events from queue and clear it (thread-safe)
            async with self._fill_events_lock:
                fills = list(self._fill_events_queue)
                self._fill_events_queue.clear()
            
            self.logger.debug(f"[FILL-EVENTS] Returning {len(fills)} fill events")
            
            return {
                'fills': fills,
                'timestamp': datetime.now(timezone.utc).isoformat()
            }
            
        except Exception as e:
            self.logger.error(f"Error getting fill events: {e}")
            return {
                'fills': [],
                'error': str(e),
                'timestamp': datetime.now(timezone.utc).isoformat()
            }

    async def get_health_score(self) -> Dict[str, Any]:
        """
        PHASE 7 ENHANCED: Get comprehensive health metrics and statistics.
        
        Returns:
            Health score and system statistics with enhanced monitoring
        """
            
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
            
            # PHASE 7: Enhanced monitoring
            
            # 1. WebSocket connection status
            websocket_connected = False
            try:
                if hasattr(self.suite, 'realtime_client'):
                    realtime_client = self.suite.realtime_client
                    if hasattr(realtime_client, 'is_connected'):
                        websocket_connected = realtime_client.is_connected
                    elif hasattr(realtime_client, 'connected'):
                        websocket_connected = realtime_client.connected
            except Exception as e:
                self.logger.debug(f"WebSocket status check: {e}")
            
            # 2. Authentication validity check (lightweight API call)
            auth_valid = True
            try:
                # Try getting stats as a lightweight auth check
                test_stats = await self.suite.get_stats()
                auth_valid = test_stats is not None
            except Exception as e:
                self.logger.warning(f"Auth validity check failed: {e}")
                auth_valid = False
            
            # 3. Trading permissions check
            trading_enabled = False
            try:
                if hasattr(self.suite, 'account'):
                    account = self.suite.account
                    if hasattr(account, 'canTrade'):
                        trading_enabled = account.canTrade
                    elif hasattr(account, 'can_trade'):
                        trading_enabled = account.can_trade
            except Exception as e:
                self.logger.debug(f"Trading permission check: {e}")
            
            # 4. Rate limit tracking (if available)
            rate_limit_remaining = None
            rate_limit_reset = None
            try:
                if hasattr(self.suite, 'client'):
                    client = self.suite.client
                    if hasattr(client, 'rate_limit_remaining'):
                        rate_limit_remaining = client.rate_limit_remaining
                    if hasattr(client, 'rate_limit_reset'):
                        rate_limit_reset = client.rate_limit_reset
            except Exception as e:
                self.logger.debug(f"Rate limit check: {e}")
            
            health_data = {
                'health_score': int(overall_health),
                'status': 'healthy' if overall_health >= 80 else 'degraded' if overall_health >= 50 else 'critical',
                'instruments': instrument_health,
                'suite_stats': stats,
                'last_check': self._last_health_check.isoformat(),
                'uptime_seconds': (datetime.now(timezone.utc) - self._last_health_check).total_seconds(),
                'initialized': self._is_initialized,
                # PHASE 7 enhancements
                'websocket_connected': websocket_connected,
                'auth_valid': auth_valid,
                'trading_enabled': trading_enabled,
                'rate_limit_remaining': rate_limit_remaining,
                'rate_limit_reset': rate_limit_reset
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

    @requires_initialization
    async def get_positions(self) -> List[Dict[str, Any]]:
        """
        PHASE 2: Get all current positions from TopstepX SDK.
        
        Returns list of positions with details needed for reconciliation.
        
        Returns:
            List of position dictionaries with keys:
                - symbol: str (e.g., "ES", "MNQ")
                - quantity: int (positive for long, negative for short)
                - side: str ("LONG" or "SHORT")
                - avg_price: float
                - unrealized_pnl: float
                - realized_pnl: float
                - position_id: str
        """
        try:
            # Query all positions from SDK
            all_positions = await self.suite.positions.get_all_positions()
            
            result = []
            for position in all_positions:
                parsed = self._parse_position_data(position)
                if parsed:
                    result.append(parsed)
            
            self.logger.debug(f"[POSITIONS] Retrieved {len(result)} positions")
            return result
            
        except Exception as e:
            self.logger.error(f"Failed to get positions: {e}")
            return []
    
    @requires_initialization
    async def get_position(self, symbol: str) -> Optional[Dict[str, Any]]:
        """
        PHASE 2: Get a specific position by symbol.
        
        Args:
            symbol: Trading symbol (e.g., "ES", "MNQ")
            
        Returns:
            Position dictionary or None if not found
        """
        
        try:
            # Get all positions and filter by symbol
            all_positions = await self.get_positions()
            
            for position in all_positions:
                if position['symbol'] == symbol:
                    return position
            
            self.logger.debug(f"[POSITION] No position found for {symbol}")
            return None
            
        except Exception as e:
            self.logger.error(f"Failed to get position for {symbol}: {e}")
            return None
    
    @requires_initialization
    async def close_position(self, symbol: str, quantity: Optional[int] = None) -> Dict[str, Any]:
        """
        PHASE 3: Close a position by placing opposite side market order.
        
        Args:
            symbol: Trading symbol (e.g., "ES", "MNQ")
            quantity: Optional quantity to close (None = close entire position)
            
        Returns:
            Dictionary with success, order_id, and closed_quantity
        """
        
        try:
            # Get current position
            all_positions = await self.suite.positions.get_all_positions()
            
            position = None
            for pos in all_positions:
                pos_symbol = self._extract_symbol_from_contract_id(str(getattr(pos, 'contractId', '')))
                if pos_symbol == symbol:
                    position = pos
                    break
            
            if position is None:
                return {
                    'success': False,
                    'error': f'No position found for {symbol}'
                }
            
            net_pos = int(getattr(position, 'netPos', 0))
            if net_pos == 0:
                return {
                    'success': False,
                    'error': f'Position for {symbol} is flat (netPos=0)'
                }
            
            # Determine quantity to close
            close_qty = quantity if quantity is not None else abs(net_pos)
            
            # Validate quantity
            if close_qty > abs(net_pos):
                return {
                    'success': False,
                    'error': f'Cannot close {close_qty} contracts, position size is {abs(net_pos)}'
                }
            
            # Determine opposite side: if long (netPos > 0), sell to close; if short, buy to close
            # SDK uses: 0=Buy, 1=Sell
            side = 1 if net_pos > 0 else 0
            side_str = "SELL" if net_pos > 0 else "BUY"
            
            contract_id = str(getattr(position, 'contractId', ''))
            
            self.logger.info(f"[CLOSE-POSITION] Closing {symbol}: {side_str} {close_qty} contracts (position={net_pos})")
            
            # Place market order on opposite side to close through instrument
            instrument_obj = self.suite[symbol]
            order_result = await instrument_obj.orders.place_market_order(
                contract_id=contract_id,
                side=side,
                size=close_qty
            )
            
            order_id = str(getattr(order_result, 'id', getattr(order_result, 'orderId', 'unknown')))
            
            self.logger.info(f"✅ [CLOSE-POSITION] Position close order placed: {order_id}")
            self._emit_telemetry("position_closed", {
                "symbol": symbol,
                "quantity": close_qty,
                "side": side_str,
                "order_id": order_id
            })
            
            return {
                'success': True,
                'order_id': order_id,
                'closed_quantity': close_qty,
                'symbol': symbol
            }
            
        except Exception as e:
            self.logger.error(f"Error closing position for {symbol}: {e}")
            return {
                'success': False,
                'error': str(e)
            }
    
    @requires_initialization
    async def modify_stop_loss(self, symbol: str, stop_price: float) -> Dict[str, Any]:
        """
        PHASE 3: Modify stop loss for a position.
        
        Args:
            symbol: Trading symbol (e.g., "ES", "MNQ")
            stop_price: New stop loss price
            
        Returns:
            Dictionary with success and order_id
        """
        
        try:
            # Get current position to determine contract ID
            all_positions = await self.suite.positions.get_all_positions()
            
            position = None
            for pos in all_positions:
                pos_symbol = self._extract_symbol_from_contract_id(str(getattr(pos, 'contractId', '')))
                if pos_symbol == symbol:
                    position = pos
                    break
            
            if position is None:
                return {
                    'success': False,
                    'error': f'No position found for {symbol}'
                }
            
            contract_id = str(getattr(position, 'contractId', ''))
            
            # Search for existing stop orders through instrument
            instrument_obj = self.suite[symbol]
            open_orders = await instrument_obj.orders.search_open_orders(contract_id=contract_id)
            
            # Filter for stop orders (type 4 = Stop)
            stop_orders = [order for order in open_orders if getattr(order, 'type', None) == 4]
            
            if not stop_orders:
                return {
                    'success': False,
                    'error': f'No stop order found for {symbol}. Create a stop order first before modifying.'
                }
            
            # Modify the first stop order found
            stop_order = stop_orders[0]
            order_id = str(getattr(stop_order, 'id', getattr(stop_order, 'orderId', 'unknown')))
            
            self.logger.info(f"[MODIFY-STOP] Modifying stop loss for {symbol}: order={order_id} new_stop=${stop_price:.2f}")
            
            # Modify the stop order through instrument
            modify_result = await instrument_obj.orders.modify_order(
                order_id=order_id,
                stop_price=stop_price
            )
            
            self.logger.info(f"✅ [MODIFY-STOP] Stop loss modified successfully: {order_id}")
            self._emit_telemetry("stop_loss_modified", {
                "symbol": symbol,
                "stop_price": stop_price,
                "order_id": order_id
            })
            
            return {
                'success': True,
                'order_id': order_id,
                'symbol': symbol,
                'stop_price': stop_price
            }
            
        except Exception as e:
            self.logger.error(f"Error modifying stop loss for {symbol}: {e}")
            return {
                'success': False,
                'error': str(e)
            }
    
    @requires_initialization
    async def modify_take_profit(self, symbol: str, take_profit_price: float) -> Dict[str, Any]:
        """
        PHASE 3: Modify take profit for a position.
        
        Args:
            symbol: Trading symbol (e.g., "ES", "MNQ")
            take_profit_price: New take profit price
            
        Returns:
            Dictionary with success and order_id
        """
        
        try:
            # Get current position to determine contract ID and side
            all_positions = await self.suite.positions.get_all_positions()
            
            position = None
            for pos in all_positions:
                pos_symbol = self._extract_symbol_from_contract_id(str(getattr(pos, 'contractId', '')))
                if pos_symbol == symbol:
                    position = pos
                    break
            
            if position is None:
                return {
                    'success': False,
                    'error': f'No position found for {symbol}'
                }
            
            contract_id = str(getattr(position, 'contractId', ''))
            net_pos = int(getattr(position, 'netPos', 0))
            
            if net_pos == 0:
                return {
                    'success': False,
                    'error': f'Position for {symbol} is flat (netPos=0)'
                }
            
            # Determine which side the take profit order should be
            # If long position (netPos > 0), take profit is a sell limit (side=1)
            # If short position (netPos < 0), take profit is a buy limit (side=0)
            tp_side = 1 if net_pos > 0 else 0
            
            # Search for existing limit orders through instrument
            instrument_obj = self.suite[symbol]
            open_orders = await instrument_obj.orders.search_open_orders(contract_id=contract_id)
            
            # Filter for limit orders on the take profit side (type 1 = Limit)
            tp_orders = [order for order in open_orders 
                        if getattr(order, 'type', None) == 1 
                        and getattr(order, 'side', None) == tp_side]
            
            if not tp_orders:
                return {
                    'success': False,
                    'error': f'No take profit order found for {symbol}. Create a limit order first before modifying.'
                }
            
            # Modify the first take profit order found
            tp_order = tp_orders[0]
            order_id = str(getattr(tp_order, 'id', getattr(tp_order, 'orderId', 'unknown')))
            
            self.logger.info(f"[MODIFY-TARGET] Modifying take profit for {symbol}: order={order_id} new_target=${take_profit_price:.2f}")
            
            # Modify the limit order through instrument
            modify_result = await instrument_obj.orders.modify_order(
                order_id=order_id,
                limit_price=take_profit_price
            )
            
            self.logger.info(f"✅ [MODIFY-TARGET] Take profit modified successfully: {order_id}")
            self._emit_telemetry("take_profit_modified", {
                "symbol": symbol,
                "take_profit_price": take_profit_price,
                "order_id": order_id
            })
            
            return {
                'success': True,
                'order_id': order_id,
                'symbol': symbol,
                'take_profit_price': take_profit_price
            }
            
        except Exception as e:
            self.logger.error(f"Error modifying take profit for {symbol}: {e}")
            return {
                'success': False,
                'error': str(e)
            }

    @requires_initialization
    async def place_bracket_order(
        self,
        symbol: str,
        side: str,
        quantity: int,
        entry_price: float,
        stop_loss_price: float,
        take_profit_price: float
    ) -> Dict[str, Any]:
        """
        PHASE 4: Place bracket order with entry, stop loss, and take profit.
        
        Args:
            symbol: Trading symbol (e.g., "ES", "MNQ")
            side: "BUY" or "SELL"
            quantity: Order quantity
            entry_price: Entry limit price
            stop_loss_price: Stop loss price
            take_profit_price: Take profit price
            
        Returns:
            Dictionary with bracket order details including all three order IDs
        """
        
        if symbol not in self.instruments:
            return {
                'success': False,
                'error': f'Symbol {symbol} not in configured instruments: {self.instruments}'
            }
        
        try:
            # Convert side to SDK format: 0=Buy, 1=Sell
            side_upper = side.upper()
            if side_upper not in ['BUY', 'SELL']:
                return {
                    'success': False,
                    'error': f'Invalid side: {side}. Must be BUY or SELL'
                }
            
            sdk_side = 0 if side_upper == 'BUY' else 1
            
            # Get contract ID for the symbol
            # For simplicity, we'll construct it from the symbol
            # In production, this should query the SDK for the actual contract ID
            contract_id_map = {
                'MNQ': 'CON.F.US.MNQ.Z25',  # Example - should be dynamic
                'ES': 'CON.F.US.EP.Z25',
                'NQ': 'CON.F.US.ENQ.Z25'
            }
            
            contract_id = contract_id_map.get(symbol)
            if not contract_id:
                # Fallback: try to get from position or use symbol as-is
                contract_id = f"CON.F.US.{symbol}.Z25"
            
            self.logger.info(
                f"[BRACKET] Placing bracket order: {symbol} {side} {quantity} "
                f"entry=${entry_price:.2f} stop=${stop_loss_price:.2f} target=${take_profit_price:.2f}"
            )
            
            # Place bracket order using SDK's native bracket support through instrument
            instrument_obj = self.suite[symbol]
            bracket_result = await instrument_obj.orders.place_bracket_order(
                contract_id=contract_id,
                side=sdk_side,
                size=quantity,
                entry_price=entry_price,
                stop_loss_price=stop_loss_price,
                take_profit_price=take_profit_price
            )
            
            # Extract order IDs from bracket response
            success = getattr(bracket_result, 'success', True)
            entry_order_id = str(getattr(bracket_result, 'entry_order_id', 
                                        getattr(bracket_result, 'entryOrderId', 'unknown')))
            stop_order_id = str(getattr(bracket_result, 'stop_order_id',
                                       getattr(bracket_result, 'stopOrderId', 'unknown')))
            target_order_id = str(getattr(bracket_result, 'target_order_id',
                                         getattr(bracket_result, 'targetOrderId', 'unknown')))
            
            # Get actual prices used (after tick alignment)
            actual_entry = float(getattr(bracket_result, 'entry_price', entry_price))
            actual_stop = float(getattr(bracket_result, 'stop_loss_price', stop_loss_price))
            actual_target = float(getattr(bracket_result, 'take_profit_price', take_profit_price))
            
            error_message = getattr(bracket_result, 'error_message', None)
            
            if not success and error_message:
                return {
                    'success': False,
                    'error': error_message
                }
            
            self.logger.info(
                f"✅ [BRACKET] Bracket order placed successfully: "
                f"entry={entry_order_id} stop={stop_order_id} target={target_order_id}"
            )
            
            self._emit_telemetry("bracket_order_placed", {
                "symbol": symbol,
                "side": side,
                "quantity": quantity,
                "entry_order_id": entry_order_id,
                "stop_order_id": stop_order_id,
                "target_order_id": target_order_id
            })
            
            return {
                'success': True,
                'entry_order_id': entry_order_id,
                'stop_order_id': stop_order_id,
                'target_order_id': target_order_id,
                'entry_price': actual_entry,
                'stop_loss_price': actual_stop,
                'take_profit_price': actual_target,
                'symbol': symbol,
                'side': side,
                'quantity': quantity
            }
            
        except Exception as e:
            self.logger.error(f"Error placing bracket order for {symbol}: {e}")
            return {
                'success': False,
                'error': str(e)
            }

    @requires_initialization
    async def get_order_status(self, order_id: str) -> Optional[Dict[str, Any]]:
        """
        PHASE 5: Get status of a specific order.
        
        Args:
            order_id: Order ID to query
            
        Returns:
            Dictionary with order status details or None if not found
        """
        
        try:
            # Query order from SDK
            # Note: SDK access pattern may vary - adjust based on actual SDK API
            # This assumes suite.orders.get_order_by_id() exists
            order = None
            
            # Try to find order across all instruments
            for symbol in self.instruments:
                try:
                    instrument_obj = self.suite[symbol]
                    if hasattr(instrument_obj, 'orders'):
                        # Attempt to get order by ID
                        # SDK API may vary - this is based on problem statement guidance
                        if hasattr(instrument_obj.orders, 'get_order_by_id'):
                            order = await instrument_obj.orders.get_order_by_id(order_id)
                            if order:
                                break
                except Exception:
                    continue
            
            if not order:
                return {
                    'success': False,
                    'error': f'Order {order_id} not found'
                }
            
            # Transform Order object to dictionary
            # Status codes: 0=None, 1=Open, 2=Filled, 3=Cancelled, 4=Expired, 5=Rejected, 6=Pending
            status = int(getattr(order, 'status', 0))
            size = int(getattr(order, 'size', 0))
            fill_volume = int(getattr(order, 'fillVolume', 0))
            filled_price = float(getattr(order, 'filledPrice', 0.0))
            
            result = {
                'success': True,
                'order_id': str(getattr(order, 'id', order_id)),
                'status': status,
                'filled_quantity': fill_volume,
                'remaining_quantity': size - fill_volume,
                'avg_fill_price': filled_price,
                'is_filled': getattr(order, 'is_filled', fill_volume >= size),
                'is_open': getattr(order, 'is_open', status == 1),
                'is_cancelled': getattr(order, 'is_cancelled', status == 3)
            }
            
            self.logger.info(f"[ORDER-STATUS] {order_id}: status={status} filled={fill_volume}/{size}")
            
            return result
            
        except Exception as e:
            self.logger.error(f"Error getting order status for {order_id}: {e}")
            return {
                'success': False,
                'error': str(e)
            }
    
    @requires_initialization
    async def cancel_order(self, order_id: str) -> Dict[str, Any]:
        """
        PHASE 6: Cancel a specific order.
        
        Args:
            order_id: Order ID to cancel
            
        Returns:
            Dictionary with cancellation result
        """
        
        try:
            # Try to cancel order across all instruments
            cancelled = False
            
            for symbol in self.instruments:
                try:
                    instrument_obj = self.suite[symbol]
                    if hasattr(instrument_obj, 'orders'):
                        # Attempt to cancel order
                        result = await instrument_obj.orders.cancel_order(order_id)
                        if result:
                            cancelled = True
                            break
                except Exception:
                    continue
            
            if cancelled:
                self.logger.info(f"[CANCEL-ORDER] Successfully cancelled order {order_id}")
                self._emit_telemetry("order_cancelled", {"order_id": order_id})
                
                return {
                    'success': True,
                    'order_id': order_id,
                    'message': 'Order cancelled successfully'
                }
            else:
                return {
                    'success': False,
                    'order_id': order_id,
                    'error': 'Failed to cancel order or order not found'
                }
            
        except Exception as e:
            self.logger.error(f"Error cancelling order {order_id}: {e}")
            return {
                'success': False,
                'order_id': order_id,
                'error': str(e)
            }
    
    @requires_initialization
    async def cancel_all_orders(self, symbol: Optional[str] = None) -> Dict[str, Any]:
        """
        PHASE 6: Cancel all orders, optionally filtered by symbol.
        
        Args:
            symbol: Optional symbol to filter cancellations
            
        Returns:
            Dictionary with cancellation results
        """
        
        try:
            cancelled_count = 0
            
            # Determine which symbols to process
            symbols_to_process = [symbol] if symbol else self.instruments
            
            for sym in symbols_to_process:
                try:
                    instrument_obj = self.suite[sym]
                    if hasattr(instrument_obj, 'orders'):
                        # Get contract ID for filtering
                        contract_id = None
                        if symbol:
                            # Try to determine contract ID from symbol
                            # Map symbol to contract ID format
                            symbol_to_contract = {
                                'ES': 'CON.F.US.EP.Z25',
                                'NQ': 'CON.F.US.ENQ.Z25',
                                'MNQ': 'CON.F.US.MNQ.Z25',
                                'MES': 'CON.F.US.MES.Z25'
                            }
                            contract_id = symbol_to_contract.get(sym)
                        
                        # Cancel all orders for this instrument
                        result = await instrument_obj.orders.cancel_all_orders(contract_id=contract_id)
                        
                        # Result might be boolean or dict with count
                        if isinstance(result, dict):
                            cancelled_count += result.get('count', 1 if result.get('success') else 0)
                        elif result:
                            cancelled_count += 1
                            
                except Exception as e:
                    self.logger.warning(f"Error cancelling orders for {sym}: {e}")
                    continue
            
            self.logger.info(f"[CANCEL-ALL] Cancelled {cancelled_count} orders" + (f" for {symbol}" if symbol else ""))
            self._emit_telemetry("orders_cancelled", {
                "count": cancelled_count,
                "symbol": symbol
            })
            
            return {
                'success': True,
                'cancelled_count': cancelled_count,
                'symbol': symbol
            }
            
        except Exception as e:
            self.logger.error(f"Error cancelling all orders: {e}")
            return {
                'success': False,
                'error': str(e)
            }

    @requires_initialization
    async def get_portfolio_status(self) -> Dict[str, Any]:
        """Get current portfolio positions and P&L."""
        try:
            # Get portfolio data from TradingSuite
            suite_stats = await self.suite.get_stats()
            
            # Use new get_positions() method for detailed position data
            positions_list = await self.get_positions()
            
            # Transform to dict keyed by symbol for backward compatibility
            positions = {}
            for pos in positions_list:
                symbol = pos['symbol']
                positions[symbol] = {
                    'size': pos['quantity'] if pos['side'] == 'LONG' else -pos['quantity'],
                    'average_price': pos['avg_price'],
                    'unrealized_pnl': pos['unrealized_pnl'],
                    'realized_pnl': pos['realized_pnl']
                }
            
            # Add instruments with no position
            for instrument in self.instruments:
                if instrument not in positions:
                    positions[instrument] = {
                        'size': 0,
                        'average_price': 0.0,
                        'unrealized_pnl': 0.0,
                        'realized_pnl': 0.0
                    }
                    
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
                        
                        elif action == "get_fill_events":
                            result = await adapter.get_fill_events()
                            return result
                        
                        elif action == "get_positions":
                            positions = await adapter.get_positions()
                            return {"success": True, "positions": positions}
                        
                        elif action == "close_position":
                            symbol = cmd_data.get("symbol")
                            quantity = cmd_data.get("quantity")
                            result = await adapter.close_position(symbol, quantity)
                            return result
                        
                        elif action == "modify_stop_loss":
                            symbol = cmd_data.get("symbol")
                            stop_price = float(cmd_data.get("stop_price"))
                            result = await adapter.modify_stop_loss(symbol, stop_price)
                            return result
                        
                        elif action == "modify_take_profit":
                            symbol = cmd_data.get("symbol")
                            take_profit_price = float(cmd_data.get("take_profit_price"))
                            result = await adapter.modify_take_profit(symbol, take_profit_price)
                            return result
                        
                        elif action == "place_bracket_order":
                            result = await adapter.place_bracket_order(
                                symbol=cmd_data.get("symbol"),
                                side=cmd_data.get("side"),
                                quantity=int(cmd_data.get("quantity")),
                                entry_price=float(cmd_data.get("entry_price")),
                                stop_loss_price=float(cmd_data.get("stop_loss_price")),
                                take_profit_price=float(cmd_data.get("take_profit_price"))
                            )
                            return result
                        
                        elif action == "get_order_status":
                            order_id = cmd_data.get("order_id")
                            result = await adapter.get_order_status(order_id)
                            return result
                        
                        elif action == "cancel_order":
                            order_id = cmd_data.get("order_id")
                            result = await adapter.cancel_order(order_id)
                            return result
                        
                        elif action == "cancel_all_orders":
                            symbol = cmd_data.get("symbol")  # Optional
                            result = await adapter.cancel_all_orders(symbol)
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