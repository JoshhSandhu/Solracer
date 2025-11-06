"""
Chart data service for fetching and normalizing token price data.

This service integrates with Birdeye API to fetch 24-hour candlestick data
for Solana tokens, normalizes the data to 0-1 range, and caches it in the database.
"""

import httpx
import os
from typing import List, Optional, Dict, Any
from datetime import datetime, timedelta
from sqlalchemy.orm import Session
from app.models import Token
import json
import logging

logger = logging.getLogger(__name__)

# Birdeye API configuration
BIRDEYE_API_BASE = "https://public-api.birdeye.so"
BIRDEYE_API_KEY = os.getenv("BIRDEYE_API_KEY", "")  # Optional API key for higher rate limits


class ChartDataService:
    """
    Service for fetching and processing token chart data.
    
    Fetches 24-hour candlestick data from Birdeye API and normalizes
    it to 0-1 range for track generation.
    """
    
    def __init__(self):
        self.api_base = BIRDEYE_API_BASE
        self.api_key = BIRDEYE_API_KEY
        self.cache_duration_hours = 1  # Cache chart data for 1 hour
    
    async def fetch_candlestick_data(
        self,
        mint_address: str,
        interval: str = "1h",
        limit: int = 24
    ) -> Optional[List[Dict[str, Any]]]:
        """
        Fetch candlestick data from Birdeye API.
        
        Args:
            mint_address: Solana token mint address
            interval: Time interval (1m, 5m, 15m, 1h, 4h, 1d)
            limit: Number of candles to fetch (max 1000)
        
        Returns:
            List of candlestick data or None if fetch fails
        """
        url = f"{self.api_base}/defi/ohlcv"
        
        # Get previous calendar day (00:00 to 23:59:59)
        now = datetime.now()
        # Start of previous day (00:00:00)
        previous_day_start = (now - timedelta(days=1)).replace(hour=0, minute=0, second=0, microsecond=0)
        # End of previous day (23:59:59)
        previous_day_end = previous_day_start.replace(hour=23, minute=59, second=59)
        
        logger.info(f"Fetching chart data for {mint_address}: {previous_day_start.strftime('%Y-%m-%d %H:%M:%S')} to {previous_day_end.strftime('%Y-%m-%d %H:%M:%S')}")
        
        params = {
            "address": mint_address,
            "type": interval,
            "time_from": int(previous_day_start.timestamp()),
            "time_to": int(previous_day_end.timestamp()),
        }
        
        headers = {}
        if self.api_key:
            headers["X-API-KEY"] = self.api_key
        
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                response = await client.get(url, params=params, headers=headers)
                response.raise_for_status()
                data = response.json()
                
                # Birdeye API returns data in 'data' field
                if "data" in data and "items" in data["data"]:
                    candles = data["data"]["items"]
                    # Sort by timestamp (oldest first)
                    candles.sort(key=lambda x: x.get("unixTime", 0))
                    return candles[:limit]
                else:
                    logger.warning(f"Unexpected Birdeye API response format for {mint_address}")
                    return None
                    
        except httpx.HTTPError as e:
            logger.error(f"HTTP error fetching chart data for {mint_address}: {e}")
            return None
        except Exception as e:
            logger.error(f"Error fetching chart data for {mint_address}: {e}")
            return None
    
    def normalize_chart_data(
        self,
        candles: List[Dict[str, Any]],
        num_samples: int = 1000
    ) -> List[Dict[str, float]]:
        """
        Normalize candlestick data to 0-1 range.
        
        This function:
        1. Extracts close prices from candlesticks
        2. Finds min/max values
        3. Normalizes to 0-1 range
        4. Interpolates to desired number of samples
        
        Args:
            candles: List of candlestick data from API
            num_samples: Number of normalized samples to generate (default: 1000)
        
        Returns:
            List of normalized samples with x (0-1) and y (0-1) values
        """
        if not candles:
            raise ValueError("Cannot normalize empty candle data")
        
        # Extract close prices (or use high/low average for smoother tracks)
        prices = []
        timestamps = []
        
        for candle in candles:
            # Use close price, or average of high/low if close not available
            close = candle.get("close", None)
            if close is None:
                high = candle.get("high", 0)
                low = candle.get("low", 0)
                close = (high + low) / 2 if (high and low) else 0
            
            if close > 0:  # Only include valid prices
                prices.append(float(close))
                timestamps.append(candle.get("unixTime", 0))
        
        if not prices:
            raise ValueError("No valid prices found in candle data")
        
        # Find min and max for normalization
        min_price = min(prices)
        max_price = max(prices)
        price_range = max_price - min_price
        
        if price_range == 0:
            # All prices are the same, return flat line at 0.5
            return [
                {"x": i / (num_samples - 1), "y": 0.5}
                for i in range(num_samples)
            ]
        
        # Normalize prices to 0-1 range
        normalized_prices = [
            (price - min_price) / price_range
            for price in prices
        ]
        
        # Interpolate to desired number of samples
        samples = []
        num_candles = len(normalized_prices)
        
        for i in range(num_samples):
            x = i / (num_samples - 1)  # Normalized X position (0-1)
            
            # Map X to candle index
            candle_index = x * (num_candles - 1)
            lower_index = int(candle_index)
            upper_index = min(lower_index + 1, num_candles - 1)
            
            # Linear interpolation
            t = candle_index - lower_index
            y = normalized_prices[lower_index] * (1 - t) + normalized_prices[upper_index] * t
            
            # Clamp to 0-1 range (should already be in range, but ensure)
            y = max(0.0, min(1.0, y))
            
            samples.append({"x": x, "y": y})
        
        return samples
    
    async def get_chart_data_for_token(
        self,
        db: Session,
        token: Token,
        force_refresh: bool = False
    ) -> Optional[List[Dict[str, float]]]:
        """
        Get normalized chart data for a token, using cache if available.
        
        Args:
            db: Database session
            token: Token model instance
            force_refresh: If True, fetch fresh data even if cache is valid
        
        Returns:
            List of normalized samples or None if fetch fails
        """
        # Check cache validity
        cache_valid = False
        if token.last_chart_update and not force_refresh:
            cache_age = datetime.now(token.last_chart_update.tzinfo) - token.last_chart_update
            cache_valid = cache_age < timedelta(hours=self.cache_duration_hours)
        
        # Try to use cached data if available and valid
        if cache_valid and token.cached_chart_data:
            try:
                cached_data = json.loads(token.cached_chart_data)
                if cached_data:
                    logger.info(f"Using cached chart data for {token.symbol}")
                    return cached_data
            except (json.JSONDecodeError, AttributeError):
                logger.warning(f"Failed to parse cached chart data for {token.symbol}")
        
        # Fetch fresh data
        logger.info(f"Fetching fresh chart data for {token.symbol} ({token.mint_address})")
        candles = await self.fetch_candlestick_data(token.mint_address)
        
        if not candles:
            logger.error(f"Failed to fetch chart data for {token.symbol}")
            return None
        
        # Normalize the data
        try:
            normalized_samples = self.normalize_chart_data(candles)
        except Exception as e:
            logger.error(f"Failed to normalize chart data for {token.symbol}: {e}")
            return None
        
        # Cache the normalized data
        try:
            token.cached_chart_data = json.dumps(normalized_samples)
            token.last_chart_update = datetime.now()
            db.commit()
            logger.info(f"Cached chart data for {token.symbol}")
        except Exception as e:
            logger.error(f"Failed to cache chart data for {token.symbol}: {e}")
            db.rollback()
        
        return normalized_samples


# Global service instance
chart_data_service = ChartDataService()

