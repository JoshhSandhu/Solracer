-- Migration: Add difficulty classification to track_buckets
-- Difficulty: 0 = Easy, 1 = Medium, 2 = Hard
-- Default 1 (Medium) is safe for existing rows.

ALTER TABLE track_buckets
  ADD COLUMN IF NOT EXISTS difficulty SMALLINT NOT NULL DEFAULT 1;

-- Index for future matchmaking queries: WHERE token_mint = ? AND difficulty = ?
CREATE INDEX IF NOT EXISTS idx_track_buckets_token_difficulty
  ON track_buckets (token_mint, difficulty);
