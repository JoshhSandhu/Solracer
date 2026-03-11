# solracer-program

Anchor program for Solracer race escrow and settlement.

Program ID (declare_id):
- `2g9tQ4g6Qki95UBTGN4NcQ4ggpz5XRa6eQJ8MCuznr8S`

## What This Program Does

The program owns race escrow, accepts player results, settles winners, and pays the winner.

Core instruction flow:
1. `create_race`
2. `join_race`
3. `submit_result` (player wallet or delegated session key)
4. `settle_race`
5. `claim_prize` (winner wallet or delegated session key)

Session delegation instruction:
- `delegate_session`

## Accounts

### `Race`
Stores race configuration and lifecycle state:
- race identity and config (`race_id`, `token_mint`, `entry_fee_sol`)
- players (`player1`, `player2`)
- status (`Waiting`, `Active`, `Settled`)
- results (`player1_result`, `player2_result`)
- winner and escrow amount

### `PlayerSession`
Stores delegated session authority per `(race_id_hash, player_wallet)` for silent gameplay/result transactions.

## Repository Layout

- `programs/solracer-program/src/lib.rs` - on-chain program logic
- `tests/solracer-program.ts` - Anchor integration tests
- `Anchor.toml` - localnet config and scripts

## Local Build

```bash
cd solracer-program
anchor build
```

## Tests

```bash
cd solracer-program
anchor test
```

Or run script from `Anchor.toml`:

```bash
yarn run ts-mocha -p ./tsconfig.json -t 1000000 "tests/**/*.ts"
```

## Notes for Backend Integration

`backend-ts` instruction builders in `../backend-ts/src/services/program.ts` and PDA derivation in `../backend-ts/src/services/pda.ts` must stay aligned with:
- account ordering
- instruction discriminators
- PDA seed rules

If you change this program, regenerate/update client-side instruction builders before deploying.

