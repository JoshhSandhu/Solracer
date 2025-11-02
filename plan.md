# Solracer Unity Game Implementation Plan

## Project Structure

```
solracer/
├── client-unity/          # Unity 6000.2.6f2 project
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── Game/
│   │   │   ├── Network/
│   │   │   ├── UI/
│   │   │   ├── Auth/        # Privy integration
│   │   │   └── Utils/
│   │   ├── Scenes/
│   │   └── Prefabs/
│   └── ProjectSettings/
├── backend/               # FastAPI Python backend
│   ├── app.py
│   ├── jupiter.py
│   ├── db.py
│   └── curated_tokens.json
├── program/               # Solana Rust program
│   └── src/
│       └── lib.rs
└── docs/
    ├── DEMO.md
    ├── API.md
    └── PROGRAM.md
```

## Implementation Phases

### Phase 1: Unity Core Game (D1-2)

**Unity Setup (2D)**

- Create Unity 6000.2.6f2 project with 2D Core template
- Configure Android Build Support enabled
- Configure Android build settings (target API level, minimum SDK version)
- Set Android as active build target platform
- Landscape orientation locked (auto-rotate disabled)
- Set up scene structure: TokenPicker, ModeSelection, Race, Results
- Configure Fixed Timestep (0.0167) for deterministic physics
- Set up 2D camera (Orthographic) for side-scrolling view
- Configure Android permissions (INTERNET, ACCESS_NETWORK_STATE)

**Bike Physics & Controls (2D)**

- 2D bike controller script with Accelerate/Brake inputs
- Touch input handling for mobile (on-screen buttons or touch zones)
- 2D physics-based movement system (Rigidbody2D, 2D colliders)
- Bike rotates along Z-axis to follow track slope
- Speed and timer HUD components
- Mobile-friendly UI with appropriate button sizes and safe area handling

**Track Generation (Mock - 2D)**

- TrackGenerator script to build 2D spline/polyline from samples
- Mock data provider returning 1000 normalized points
- Visual track rendering as 2D LineRenderer or sprite path
- 2D camera follows bike (orthographic, side-scrolling view)

### Phase 2: Privy Authentication Setup (D1)

**Privy SDK Integration**

- Add Privy SDK to Unity 6000.2.6f2 project
- Configure Privy authentication (email, SMS, passkeys, social logins)
- Set up wallet connection flow via Privy
- Create test scene with authentication UI
- Test signing a "hello" message to verify integration works
- Verify wallet signature verification on backend/test endpoint

### Phase 3: Backend Foundation (D1-2)

**FastAPI Setup**

- Project structure with required dependencies
- Database schema (Postgres/Supabase): races, results, payouts tables
- Basic endpoints: GET /tokens, GET /track (mock implementation)

**Chart Data Integration (D3-4)**

- Chart data fetcher (Birdeye/Dexscreener API integration)
- 24-hour candlestick data retrieval and caching
- Normalization algorithm (0..1 range)
- Seed generation for deterministic replay

### Phase 4: Solana Program (D3-4)

**Program Structure**

- Native Rust program (no Anchor)
- Race PDA account structure
- Vault PDA for SOL escrow
- Instructions: create_race, join_race, settle_race
- Event emission: RaceSettled with required data

### Phase 5: Unity Track Integration (D3-4)

**Real Chart Integration**

- API client for GET /track endpoint
- Token picker UI with 3 tokens (SOL, BONK, META) and sparklines
- Track generation from real chart samples
- Practice mode with local leaderboard storage

### Phase 6: Competitive Flow (D5-8)

**Solana Integration**

- Transaction signing via Privy SDK (no full Solana SDK needed in Unity)
- Backend handles all Solana transaction building and submission
- Unity only signs raw transaction bytes received from backend

**Race Creation/Joining**

- POST /create_or_join_race integration (backend builds transaction)
- Transaction signing via Privy SDK (Unity signs raw bytes)
- POST /submit_transaction (backend submits signed transaction to Solana)
- SOL entry fee transfer to vault (handled by backend)
- Race state polling

**Result Submission**

- Input trace recording (button down/up timestamps)
- Input hash generation
- POST /submit_result with race_id, time_ms, input_hash
- Race status polling endpoint integration

**Backend Race Management**

- Race matching logic (create or join existing)
- Result storage and validation
- Server-side replay verification (±50ms tolerance)
- Settle race trigger (after both results submitted)

### Phase 7: Payout System (D9-10)

**Jupiter Integration**

- Jupiter swap service (SOL → token)
- Quote fetching with slippage (0.5-1.0%)
- Swap execution via Swap Agent keypair
- ATA creation/checking for winner

**Backend Worker**

- Program log subscription for RaceSettled events
- Swap workflow: quote → swap → transfer token to winner
- Fallback logic: send SOL if swap fails/illiquid
- Payout status tracking (SWAPPING → PAID/FALLBACK_SOL)

**Unity Payout UI**

- Result screen with payout status
- "Swapping prize to $TOKEN..." loading state
- Transaction links (Solscan) for swap and transfer
- Final "Paid" confirmation

### Phase 8: Polish & QA (D11-14)

**Leaderboard System**

- Competitive leaderboard (backend query)
- Practice leaderboard (local storage)
- UI implementation

**Error Handling & Recovery**

- Network error states
- Transaction failure handling
- Race recovery mechanisms
- Idempotent operations (double-settle protection)

**Mobile Optimization & Testing**

- Performance profiling on Seeker mobile device
- Memory usage optimization
- Battery consumption testing
- Build APK/AAB for Android distribution
- On-device testing on Seeker mobile
- Touch input responsiveness validation
- UI scaling for different screen resolutions

**Testing & Documentation**

- Full test checklist execution (including mobile-specific tests)
- DEMO.md creation
- API.md documentation
- PROGRAM.md with account layouts and instruction formats

## Key Technical Decisions

**Unity Version:** 6000.2.6f2

**Platform Target:** Android (Seeker mobile device)

**Wallet/Auth:** Privy SDK for authentication and wallet abstraction

**Solana Operations:** Backend handles all Solana transactions (Unity only signs via Privy)

**Physics:** Fixed timestep (0.0167) for deterministic replay

**Network:** REST API with polling for async race status

**Solana Program:** Native Rust program (no Anchor) for simplicity

**Swap Service:** Jupiter aggregator for token swaps

**Database:** Postgres (Supabase) for race state management

## Critical Implementation Details

1. **Core Game First**: Build core game mechanics (bike controller, track generation) before integrating authentication
2. **Mobile-First Design**: Touch controls and mobile UI must be designed from the start, not as an afterthought
3. **Deterministic Replay**: Fixed timestep + physics seed ensure server can verify client inputs
4. **Chart Normalization**: All Y samples normalized to 0..1 range for consistent track heights
5. **Token Configuration**: Hardcoded 3 tokens in curated_tokens.json with verified mints
6. **Entry Fees**: Configurable 0.005-0.02 SOL range
7. **Anti-cheat**: Input trace hash + server replay validation prevents obvious cheating
8. **Fallback Safety**: Always falls back to SOL if swap fails to ensure winner gets paid
9. **Android Build Verification**: Unity 6000.2.6f2 Android Build Support must be installed and verified before development
10. **Seeker Device Testing**: Regular testing on Seeker mobile device throughout development, not just at the end