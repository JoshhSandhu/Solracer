"""
Quick test to verify previous calendar day calculation.
"""

from datetime import datetime, timedelta

# Simulate what the code does
now = datetime.now()
previous_day_start = (now - timedelta(days=1)).replace(hour=0, minute=0, second=0, microsecond=0)
previous_day_end = previous_day_start.replace(hour=23, minute=59, second=59)

print(f"Current time: {now.strftime('%Y-%m-%d %H:%M:%S')}")
print(f"Previous day start: {previous_day_start.strftime('%Y-%m-%d %H:%M:%S')}")
print(f"Previous day end: {previous_day_end.strftime('%Y-%m-%d %H:%M:%S')}")
print(f"\nUnix timestamps:")
print(f"time_from: {int(previous_day_start.timestamp())}")
print(f"time_to: {int(previous_day_end.timestamp())}")
print(f"\nDuration: {(previous_day_end - previous_day_start).total_seconds() / 3600:.2f} hours")

