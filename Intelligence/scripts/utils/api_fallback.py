#!/usr/bin/env python3
"""
API fallback handler - surfaces upstream failures instead of hiding them with mock data.
Updated per AUDIT_CATEGORY_GUIDEBOOK.md Intelligence section requirements.
"""
import requests
import json
import logging
from datetime import datetime
from typing import Optional, Dict, Any

# Configure logging to surface failures
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class APIFailureError(Exception):
    """Raised when API is unavailable and no fallback is appropriate"""
    pass

def fetch_with_transparency(url: str, params: Optional[Dict] = None, timeout: int = 10, 
                          allow_fallback: bool = False) -> Dict[str, Any]:
    """
    Fetch data with transparent error reporting.
    
    Args:
        url: API endpoint URL
        params: Optional query parameters
        timeout: Request timeout in seconds
        allow_fallback: Whether to allow fallback behavior (opt-in only)
        
    Returns:
        API response data
        
    Raises:
        APIFailureError: When API is down and fallback is not allowed
    """
    try:
        logger.info(f"Attempting API call to {url}")
        response = requests.get(url, params=params, timeout=timeout)
        
        if response.status_code == 200:
            logger.info(f"API call successful: {url}")
            return response.json()
        else:
            error_details = {
                'url': url,
                'status_code': response.status_code,
                'reason': response.reason,
                'timestamp': datetime.utcnow().isoformat(),
                'response_text': response.text[:500]  # Limit for logging
            }
            logger.error(f"API call failed with status {response.status_code}: {error_details}")
            
    except requests.exceptions.Timeout:
        error_details = {
            'url': url,
            'error_type': 'timeout',
            'timeout_seconds': timeout,
            'timestamp': datetime.utcnow().isoformat()
        }
        logger.error(f"API call timed out: {error_details}")
        
    except requests.exceptions.ConnectionError as e:
        error_details = {
            'url': url,
            'error_type': 'connection_error',
            'error_message': str(e),
            'timestamp': datetime.utcnow().isoformat()
        }
        logger.error(f"API connection failed: {error_details}")
        
    except Exception as e:
        error_details = {
            'url': url,
            'error_type': 'unexpected_error',
            'error_message': str(e),
            'timestamp': datetime.utcnow().isoformat()
        }
        logger.error(f"Unexpected API error: {error_details}")
    
    # Handle failure based on fallback policy
    if allow_fallback:
        logger.warning(f"API unavailable, returning fallback response (explicitly allowed)")
        return {
            'status': 'fallback',
            'timestamp': datetime.utcnow().isoformat(),
            'message': 'API unavailable - fallback data provided',
            'original_url': url
        }
    else:
        # Surface the failure - do not hide it with mock data
        raise APIFailureError(f"API unavailable: {url}. Enable fallback explicitly if acceptable for this use case.")

# Legacy compatibility (deprecated - use fetch_with_transparency)
def fetch_with_fallback(url, params=None, timeout=10):
    """
    DEPRECATED: Use fetch_with_transparency with explicit allow_fallback=True
    """
    logger.warning("fetch_with_fallback is deprecated - use fetch_with_transparency with explicit fallback control")
    return fetch_with_transparency(url, params, timeout, allow_fallback=True)
