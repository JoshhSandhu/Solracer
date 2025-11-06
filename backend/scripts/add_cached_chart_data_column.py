"""
Database migration script to add cached_chart_data column to tokens table.

Run this script to add the missing column to your existing database.
"""

import sys
from pathlib import Path

# Add parent directory to path to import app modules
backend_dir = Path(__file__).parent.parent
sys.path.insert(0, str(backend_dir))

from sqlalchemy import text
from app.database import engine, SessionLocal
from dotenv import load_dotenv

# Load environment variables
load_dotenv(dotenv_path=backend_dir / ".env")


def add_cached_chart_data_column():
    """Add cached_chart_data column to tokens table if it doesn't exist."""
    db = SessionLocal()
    
    try:
        print("Checking if cached_chart_data column exists...")
        
        # Check if column exists
        check_query = text("""
            SELECT column_name 
            FROM information_schema.columns 
            WHERE table_name='tokens' AND column_name='cached_chart_data'
        """)
        result = db.execute(check_query).fetchone()
        
        if result:
            print("Column cached_chart_data already exists. Skipping migration.")
            return
        
        # Add the column
        print("Adding cached_chart_data column to tokens table...")
        alter_query = text("""
            ALTER TABLE tokens 
            ADD COLUMN cached_chart_data TEXT
        """)
        db.execute(alter_query)
        db.commit()
        
        print("Successfully added cached_chart_data column!")
        
    except Exception as e:
        db.rollback()
        print(f"Error adding column: {e}")
        raise
    finally:
        db.close()


if __name__ == "__main__":
    add_cached_chart_data_column()

