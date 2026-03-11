# solracer-er

Anchor program for Solracer ghost positions on MagicBlock Ephemeral Rollups.

Program ID (declare_id):
- `3BhDmsVJYASHEUE2DJAJr2FHjRWUCF1nwn6SraKJgoEG`

## Purpose

This program tracks low-latency ghost position updates for race participants.

It is used with session keys so gameplay updates can be submitted without repeated wallet popups.

## Instruction Set

- `init_position_pda(race_id_hash, session_key)`
  - Creates `PlayerPosition` PDA for `(race_id_hash, player_wallet)`.
- `delegate_position_pda()`
  - Delegates PDA to MagicBlock ER via `ephemeral_rollups_sdk` CPI.
- `update_position(expected_race_id_hash, x, y, speed, checkpoint_index, seq)`
  - Writes latest snapshot, enforces signer authority and monotonic `seq`.
- `close_position_pda()`
  - Closes position account and returns rent.

## Account Model

### `PlayerPosition`
Stores:
- `race_id_hash`
- `player`
- `session_key`
- `x`, `y`, `speed`
- `checkpoint_index`, `seq`
- `updated_at`, `bump`

## Repository Layout

- `programs/solracer-er/src/lib.rs` - on-chain ER logic
- `tests/solracer-er.ts` - local Anchor tests
- `tests/er-live-test.ts` - devnet + MagicBlock live integration test
- `Anchor.toml` - localnet/devnet program config

## Local Build

```bash
cd solracer-er
anchor build
```

## Tests

```bash
cd solracer-er
anchor test
```

Live ER test (requires funded keypair and network access):

```bash
npx ts-node tests/er-live-test.ts
```

## Integration Notes

- Unity and backend must derive PDA seeds exactly as this program expects:
  - `position`, `SHA256(race_id)`, `player_wallet`
- `backend-ts` currently bundles ER init+delegate instructions during create/join transaction build (`src/routes/transactions.ts`).
- Unity ghost relay endpoints (`/api/v1/ghost/*`) are currently served by `backend-ts` simulation/store layer.

