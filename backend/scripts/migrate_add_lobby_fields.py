#!/usr/bin/env python3
"""
Migration script to add lobby system fields to races table.

Adds:
- is_private (boolean, default False)
- join_code (varchar(6), unique, nullable)
- expires_at (timestamp, nullable)
- player1_ready (boolean, default False)
- player2_ready (boolean, default False)

Run with: python scripts/migrate_add_lobby_fields.py
"""

import sys
import os
from pathlib import Path

# Add backend directory to path
backend_dir = Path(__file__).parent.parent
sys.path.insert(0, str(backend_dir))

from dotenv import load_dotenv
import psycopg2
from psycopg2.extensions import ISOLATION_LEVEL_AUTOCOMMIT

# Load environment variables
load_dotenv(dotenv_path=backend_dir / ".env")

# Database connection
DATABASE_URL = os.getenv("DATABASE_URL")
if not DATABASE_URL:
    print("ERROR: DATABASE_URL not found in .env file")
    sys.exit(1)


def run_migration():
    """Run the migration to add lobby fields."""
    print("="*60)
    print("MIGRATION: Add Lobby System Fields to Races Table")
    print("="*60)
    
    try:
        # Connect to database
        conn = psycopg2.connect(DATABASE_URL)
        conn.set_isolation_level(ISOLATION_LEVEL_AUTOCOMMIT)
        cursor = conn.cursor()
        
        print("\n[1/5] Adding is_private column...")
        try:
            cursor.execute("""
                ALTER TABLE races 
                ADD COLUMN IF NOT EXISTS is_private BOOLEAN DEFAULT FALSE NOT NULL;
            """)
            print("  ✓ is_private column added")
        except Exception as e:
            print(f"  ⚠ Error (may already exist): {e}")
        
        print("\n[2/5] Adding join_code column...")
        try:
            cursor.execute("""
                ALTER TABLE races 
                ADD COLUMN IF NOT EXISTS join_code VARCHAR(6) UNIQUE;
            """)
            print("  ✓ join_code column added")
        except Exception as e:
            print(f"  ⚠ Error (may already exist): {e}")
        
        print("\n[3/5] Adding expires_at column...")
        try:
            cursor.execute("""
                ALTER TABLE races 
                ADD COLUMN IF NOT EXISTS expires_at TIMESTAMP WITH TIME ZONE;
            """)
            print("  ✓ expires_at column added")
        except Exception as e:
            print(f"  ⚠ Error (may already exist): {e}")
        
        print("\n[4/5] Adding player1_ready column...")
        try:
            cursor.execute("""
                ALTER TABLE races 
                ADD COLUMN IF NOT EXISTS player1_ready BOOLEAN DEFAULT FALSE NOT NULL;
            """)
            print("  ✓ player1_ready column added")
        except Exception as e:
            print(f"  ⚠ Error (may already exist): {e}")
        
        print("\n[5/5] Adding player2_ready column...")
        try:
            cursor.execute("""
                ALTER TABLE races 
                ADD COLUMN IF NOT EXISTS player2_ready BOOLEAN DEFAULT FALSE NOT NULL;
            """)
            print("  ✓ player2_ready column added")
        except Exception as e:
            print(f"  ⚠ Error (may already exist): {e}")
        
        print("\n[6/6] Creating indexes...")
        try:
            cursor.execute("""
                CREATE INDEX IF NOT EXISTS idx_races_is_private 
                ON races(is_private);
            """)
            print("  ✓ Index on is_private created")
        except Exception as e:
            print(f"  ⚠ Error (may already exist): {e}")
        
        try:
            cursor.execute("""
                CREATE INDEX IF NOT EXISTS idx_races_join_code 
                ON races(join_code);
            """)
            print("  ✓ Index on join_code created")
        except Exception as e:
            print(f"  ⚠ Error (may already exist): {e}")
        
        try:
            cursor.execute("""
                CREATE INDEX IF NOT EXISTS idx_races_expires_at 
                ON races(expires_at);
            """)
            print("  ✓ Index on expires_at created")
        except Exception as e:
            print(f"  ⚠ Error (may already exist): {e}")
        
        # Verify columns exist
        print("\n[VERIFY] Verifying columns...")
        cursor.execute("""
            SELECT column_name, data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_name = 'races'
            AND column_name IN ('is_private', 'join_code', 'expires_at', 'player1_ready', 'player2_ready')
            ORDER BY column_name;
        """)
        
        columns = cursor.fetchall()
        if len(columns) == 5:
            print("  ✓ All 5 columns found:")
            for col in columns:
                print(f"    - {col[0]} ({col[1]})")
        else:
            print(f"  ⚠ Warning: Expected 5 columns, found {len(columns)}")
            for col in columns:
                print(f"    - {col[0]} ({col[1]})")
        
        cursor.close()
        conn.close()
        
        print("\n" + "="*60)
        print("MIGRATION COMPLETE!")
        print("="*60)
        print("\nYou can now run the test script:")
        print("  python scripts/test_phase7.py")
        
    except Exception as e:
        print(f"\n[ERROR] Migration failed: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    run_migration()

