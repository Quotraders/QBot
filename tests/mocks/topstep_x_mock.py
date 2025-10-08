#!/usr/bin/env python3
"""
Mock TopstepX SDK for Testing

This module provides mock implementations for the TopstepX SDK components
for use in testing and development environments only.
"""

import asyncio
import uuid
from typing import Dict, Any, List, Callable, Optional
from datetime import datetime, timezone


class EventType:
    """Mock EventType enum for SDK events"""
    ORDER_FILLED = "ORDER_FILLED"
    ORDER_PLACED = "ORDER_PLACED"
    ORDER_CANCELLED = "ORDER_CANCELLED"


class MockTradingSuite:
    def __init__(self, instruments, **kwargs):
        self.instruments = instruments
        self._connected = True
        self._event_handlers: Dict[str, List[Callable]] = {}
        self.positions = MockPositionsManager()
        
    @classmethod
    async def create(cls, instruments, **kwargs):
        # Simulate SDK initialization behavior
        await asyncio.sleep(0.1)  # Simulate connection delay
        return cls(instruments, **kwargs)
    
    def on(self, event_type: str, callback: Callable):
        """Register event handler"""
        if event_type not in self._event_handlers:
            self._event_handlers[event_type] = []
        self._event_handlers[event_type].append(callback)
    
    async def _emit_event(self, event_type: str, event_data: Any):
        """Emit event to registered handlers"""
        if event_type in self._event_handlers:
            for handler in self._event_handlers[event_type]:
                await handler(event_data)
        
    def __getitem__(self, instrument):
        return MockInstrument(instrument, suite=self)
        
    async def get_stats(self):
        return {
            "total_trades": 42,
            "win_rate": 65.5,
            "total_pnl": 1250.75,
            "max_drawdown": -150.25
        }
        
    async def get_risk_metrics(self):
        return {
            "max_risk_percent": 1.0,
            "current_risk": 0.15,
            "available_buying_power": 50000.0
        }
        
    async def get_portfolio_status(self):
        return {
            "account_value": 50000.0,
            "buying_power": 45000.0,
            "day_pnl": 125.50
        }
        
    async def disconnect(self):
        self._connected = False
        
    def managed_trade(self, max_risk_percent=0.01):
        return MockManagedTradeContext(max_risk_percent)


class MockPosition:
    """Mock Position object from SDK"""
    def __init__(self, contract_id: str, net_pos: int, buy_avg: float = 0.0, sell_avg: float = 0.0):
        self.contractId = contract_id
        self.netPos = net_pos
        self.buyAvgPrice = buy_avg
        self.sellAvgPrice = sell_avg
        self.unrealizedPnl = 0.0
        self.realizedPnl = 0.0
        self.id = f"POS-{uuid.uuid4()}"
        self.type = 1 if net_pos > 0 else 2  # 1=LONG, 2=SHORT
        self.is_long = net_pos > 0
        self.is_short = net_pos < 0


class MockPositionsManager:
    """Mock positions manager for suite.positions"""
    def __init__(self):
        self._positions: List[MockPosition] = []
        
    async def get_all_positions(self) -> List[MockPosition]:
        """Get all positions"""
        return self._positions
    
    async def get_position(self, contract_id: str) -> Optional[MockPosition]:
        """Get specific position by contract ID"""
        for pos in self._positions:
            if pos.contractId == contract_id:
                return pos
        return None
    
    def _add_test_position(self, contract_id: str, net_pos: int, avg_price: float):
        """Internal method to add test positions"""
        if net_pos > 0:
            self._positions.append(MockPosition(contract_id, net_pos, buy_avg=avg_price))
        elif net_pos < 0:
            self._positions.append(MockPosition(contract_id, net_pos, sell_avg=avg_price))


class MockInstrument:
    def __init__(self, symbol, suite=None):
        self.symbol = symbol
        self.data = MockData(symbol)
        self.orders = MockOrders(symbol, suite=suite)
        self.positions = MockPositions()
        self.suite = suite
        
    async def get_position(self):
        return {
            "size": 0,
            "average_price": 0.0,
            "unrealized_pnl": 0.0,
            "realized_pnl": 0.0
        }


class MockData:
    def __init__(self, symbol):
        self.symbol = symbol
        
    async def get_current_price(self):
        # Realistic prices for MNQ and ES
        prices = {
            'MNQ': 18500.00,
            'ES': 4500.00,
            'NQ': 18500.00,
            'RTY': 2100.00,
            'YM': 35000.00
        }
        return prices.get(self.symbol, 100.00)


class MockFillEvent:
    """Mock fill event data"""
    def __init__(self, order_id: str, contract_id: str, quantity: int, price: float):
        self.orderId = order_id
        self.order_id = order_id
        self.contractId = contract_id
        self.contract_id = contract_id
        self.quantity = quantity
        self.qty = quantity
        self.price = price
        self.fill_price = price
        self.commission = 2.50
        self.liquidityType = "TAKER"
        self.liquidity_type = "TAKER"
        self.timestamp = int(datetime.now(timezone.utc).timestamp() * 1000)


class MockOrderResponse:
    """Mock order response"""
    def __init__(self, order_id: str):
        self.id = order_id
        self.orderId = order_id
        self.success = True


class MockBracketOrderResponse:
    """Mock bracket order response"""
    def __init__(self, entry_id: str, stop_id: str, target_id: str, 
                 entry_price: float, stop_price: float, target_price: float):
        self.success = True
        self.entry_order_id = entry_id
        self.entryOrderId = entry_id
        self.stop_order_id = stop_id
        self.stopOrderId = stop_id
        self.target_order_id = target_id
        self.targetOrderId = target_id
        self.entry_price = entry_price
        self.stop_loss_price = stop_price
        self.take_profit_price = target_price
        self.error_message = None


class MockOpenOrder:
    """Mock open order for searching"""
    def __init__(self, order_id: str, order_type: int, side: int, price: float):
        self.id = order_id
        self.orderId = order_id
        self.type = order_type  # 1=Limit, 4=Stop
        self.side = side  # 0=Buy, 1=Sell
        self.price = price


class MockOrders:
    def __init__(self, symbol, suite=None):
        self.symbol = symbol
        self.suite = suite
        self._open_orders = []  # Track open orders for testing
    
    async def place_market_order(self, contract_id: str, side: int, size: int):
        """Place market order (for closing positions)"""
        order_id = str(uuid.uuid4())
        return MockOrderResponse(order_id)
    
    async def search_open_orders(self, contract_id: str = None):
        """Search for open orders"""
        # Return mock orders for testing
        # Add a stop order (type 4) and limit order (type 1)
        return self._open_orders
    
    async def modify_order(self, order_id: str, stop_price: float = None, limit_price: float = None):
        """Modify an existing order"""
        return MockOrderResponse(order_id)
    
    async def place_bracket_order(self, side=None, quantity=None, stop_loss=None, take_profit=None,
                                  contract_id=None, size=None, entry_price=None, 
                                  stop_loss_price=None, take_profit_price=None):
        """Place bracket order with entry, stop, and target (supports both old and new signatures)"""
        # Handle both old signature (side, quantity, stop_loss, take_profit) 
        # and new signature (contract_id, side, size, entry_price, stop_loss_price, take_profit_price)
        
        if contract_id is not None:
            # New signature (Phase 4)
            entry_id = str(uuid.uuid4())
            stop_id = str(uuid.uuid4())
            target_id = str(uuid.uuid4())
            
            return MockBracketOrderResponse(
                entry_id, stop_id, target_id,
                entry_price or 0.0, stop_loss_price or 0.0, take_profit_price or 0.0
            )
        else:
            # Old signature (place_order backward compatibility)
            # Implement basic risk checking for validation
            if quantity and quantity > 10:  # Simple risk check for oversized orders
                raise ValueError(f"Order size {quantity} exceeds maximum allowed size of 10")
            
            order_id = str(uuid.uuid4())
            result = {
                "id": order_id,
                "entry_order_id": str(uuid.uuid4()),
                "stop_order_id": str(uuid.uuid4()),
                "target_order_id": str(uuid.uuid4()),
                "status": "accepted"
            }
            
            # Simulate a fill event after a short delay (for testing event subscription)
            if self.suite and quantity:
                async def emit_fill():
                    await asyncio.sleep(0.1)
                    # Get realistic price for the symbol
                    prices = {'MNQ': 18500.00, 'ES': 4500.00, 'NQ': 18500.00}
                    price = prices.get(self.symbol, 100.00)
                    
                    # Create mock contract ID
                    symbol_map = {'MNQ': 'MNQ', 'ES': 'EP', 'NQ': 'ENQ'}
                    contract_symbol = symbol_map.get(self.symbol, self.symbol)
                    contract_id_str = f"CON.F.US.{contract_symbol}.Z25"
                    
                    fill_event = MockFillEvent(order_id, contract_id_str, quantity, price)
                    await self.suite._emit_event(EventType.ORDER_FILLED, fill_event)
                
                # Schedule the fill event emission
                asyncio.create_task(emit_fill())
            
            return result


class MockPositions:
    def __init__(self):
        self.quantity = 0
        self.avg_price = 0.0
        self.unrealized_pnl = 0.0
        self.realized_pnl = 0.0


class MockManagedTradeContext:
    def __init__(self, max_risk_percent):
        self.max_risk_percent = max_risk_percent
        
    async def __aenter__(self):
        return self
        
    async def __aexit__(self, exc_type, exc_val, exc_tb):
        pass