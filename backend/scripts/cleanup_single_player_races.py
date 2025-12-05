"""
Utility script to clean up races that were started with only a single player.

Usage (from backend folder, with venv activated):
    python -m scripts.cleanup_single_player_races
"""

from app.database import SessionLocal
from app.models import Race, RaceResult, Payout, RaceStatus


def cleanup_single_player_races():
    db = SessionLocal()
    try:
        # Find races that never got a second player
        races = (
            db.query(Race)
            .filter(
                Race.player2_wallet.is_(None),
                Race.status != RaceStatus.CANCELLED,
            )
            .all()
        )

        print(f"Found {len(races)} races with only a single player (non-cancelled).")

        for race in races:
            print(f"Cleaning race {race.race_id} (status={race.status}, player1={race.player1_wallet})")

            # Delete any results for this race
            db.query(RaceResult).filter(RaceResult.race_id == race.id).delete()

            # Delete any payouts for this race (should normally not exist)
            db.query(Payout).filter(Payout.race_id == race.id).delete()

            # Either delete the race or mark it as cancelled.
            # To fully "clear" them, we delete.
            db.delete(race)

        db.commit()
        print("Cleanup complete.")
    finally:
        db.close()


if __name__ == "__main__":
    cleanup_single_player_races()


