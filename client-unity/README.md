# Solracer Unity Client

> Unity game client for Solracer  a competitive racing game where players race on real-time Solana token price charts. Supports MWA wallet connection, on-chain race escrow, and oracle-driven deterministic tracks.

**Tech Stack:** Unity 6000.2.6f2 · Solana Unity SDK · Privy SDK · C# · Android (Seeker)

---

## Features

- **Oracle Track System**  tracks are generated from live token price data via Backend-v2
- **Practice Mode**  use oracle tracks without wagering; mock fallback when offline
- **Competitive Mode**  on-chain escrow via Solana program; SOL entry fees; Jupiter swap payouts
- **MWA Wallet**  connects to Seeker wallet via Mobile Wallet Adapter (Solana Unity SDK)
- **Privy Wallet**  email-based embedded Solana wallet for non-Seeker users
- **Deterministic Replay**  fixed-timestep physics + input hash for anti-cheat verification
- **DebugConsole**  on-screen runtime log overlay (toggle in top-right corner)

---

## Quick Start

### Prerequisites

- Unity 6000.2.6f2 or later
- Android Build Support module installed
- Solana Seeker device (for MWA testing)
- Backend API server running (see `../backend/README.md`)
- Backend-v2 track server running (oracle track data)

### Installation

1. **Open in Unity Hub** → Add project at `client-unity/`
2. **Verify packages** in Package Manager:
   - Solana Unity SDK (`com.solana.unity_sdk`)
   - Privy SDK (under `Assets/Plugins/Privy/`)
3. **Configure Privy**  open `Assets/Scripts/Auth/PrivyConfig.cs` or the asset and set your `appId` and `clientId`
4. **Configure Build Settings** → Android platform:
   - Minimum API: 21, Target API: 33+
   - Scripting Backend: IL2CPP
   - Architecture: ARM64

---

## Configuration

### API Endpoints

`Assets/Scripts/Config/APIConfig.cs` controls all API URLs.

| Constant | Value | Used For |
|----------|-------|---------|
| `PRODUCTION_URL` | `https://api.solracer.com` | Production builds |
| `STAGING_URL` | `https://staging-api.solracer.com` | Staging builds |
| `LOCAL_URL` | `https://localhost:8000` | Editor / dev builds |
| `LOCAL_NETWORK_URL` | `192.168.29.123:8000` | **⚠ Must update before sharing builds** |
| `TRACK_API_V2_URL` | `https://api.lynxjosh.cyou` | Oracle track data (Backend-v2) |

> **Important:** `LOCAL_NETWORK_URL` is hardcoded to a developer machine IP. Update it or use `APIConfig.SetApiBaseUrl()` / PlayerPrefs override before distributing builds.

Set build define symbols in Player Settings → Scripting Define Symbols:
- `DEVELOPMENT_BUILD` → uses `LOCAL_URL`
- `STAGING_BUILD` → uses `STAGING_URL`
- `PRODUCTION_BUILD` → uses `PRODUCTION_URL`

If no define is set, `#if UNITY_EDITOR` uses localhost; all other builds use `LOCAL_NETWORK_URL`.

---

## Project Structure

```
Assets/Scripts/
├── Auth/
│   ├── AuthenticationFlowManager.cs   # Privy init, email/OTP login, wallet creation, signing
│   ├── MWAWalletAdapter.cs            # MWA wallet adapter (Solana Unity SDK)
│   ├── AuthenticationData.cs          # Static auth state (wallet address, wallet type)
│   └── PrivyConfig.cs                 # ScriptableObject: Privy appId + clientId
├── Config/
│   └── APIConfig.cs                   # Centralized API URL routing
├── Game/
│   ├── ATV/
│   │   ├── ATVController.cs           # Physics-based vehicle: torque, speed cap, mid-air rotation
│   │   ├── ATVControllerDebug.cs      # Runtime debug overlay for ATV state
│   │   └── ATVInputActions.cs         # Input System action bindings (uses "BikeControler" asset)
│   ├── Background/                    # Visual background layer scripts (11 files)
│   ├── CoinS/
│   │   ├── Coin.cs                    # Coin trigger, collection dispatch
│   │   ├── CoinCollectionManager.cs   # Tracks collected counts per coin type
│   │   ├── CoinData.cs                # CoinType enum + CoinSelectionData static state
│   │   └── CoinSpawner.cs             # Spawns coins on track; waits for TrackPoints
│   ├── Track/
│   │   ├── TrackLoader.cs             # Loads oracle tracks; falls back to mock
│   │   ├── TrackGenerator.cs          # Pure geometry: LineRenderer + EdgeCollider2D
│   │   ├── LoadedTrackData.cs         # Data model from TrackLoader → TrackGenerator
│   │   └── TrackDataProvider.cs       # Mock track fallback (sine waves + noise)
│   ├── FinishLineTrigger.cs           # Trigger collider → RaceManager.OnFinishLineCrossed()
│   ├── GameMode.cs                    # GameMode enum + GameModeData static state
│   ├── InputTraceRecorder.cs          # Records input frames; SHA-256 hash for verification
│   └── RaceManager.cs                 # Race state: countdown, respawn, finish, on-chain submit
├── Network/
│   ├── OnChainRaceManager.cs          # Static orchestrator: create/join/submit/claim on-chain
│   ├── RaceAPIClient.cs               # REST: race create, join, ready, status, cancel
│   ├── TransactionAPIClient.cs        # REST: transaction build + submit
│   ├── PayoutAPIClient.cs             # REST: payout status, process, settle, retry
│   ├── TrackAPIClientV2.cs            # REST: oracle track data from Backend-v2 ✅
│   ├── TrackBlobDecoder.cs            # Decodes Base64 height blobs from Backend-v2 ✅
│   ├── TrackModelsV2.cs               # Response models for TrackAPIClientV2 ✅
│   ├── RaceData.cs                    # Static race state: raceId, track commitment, results
│   ├── CertificateHandlerBypass.cs    # Dev-only: bypass self-signed cert validation
│   └── TrackAPIClient.cs              # ⚠ LEGACY  demo backend only, unused in production
├── UI/
│   ├── Scenes/
│   │   ├── LoginScreen.cs             # Wires Privy/MWA auth panels to AuthenticationFlowManager
│   │   ├── TokenPickerScreen.cs       # Select racing token (BONK, SOL, META)
│   │   ├── ModeSelectionScreen.cs     # Practice vs Competitive
│   │   ├── LobbyScreen.cs             # Create/join races; status polling; ready flow
│   │   ├── RaceHUD.cs                 # In-game timer + speed display
│   │   └── ResultsScreen.cs           # Results, winner/loser state, prize claiming
│   ├── MobileInputControls.cs         # On-screen buttons → ATVController input
│   ├── TransactionSigningModal.cs     # In-game modal for Privy transaction approval
│   ├── SolracerColors.cs              # ScriptableObject: brand color palette
│   └── UIStyleHelper.cs               # Styling helpers (fonts, cards, buttons)
└── Utils/
    ├── DebugConsole.cs                # On-screen log console (3-finger tap or top-right button)
    ├── ToggleSwitch.cs                # Animated toggle UI component
    ├── ToggleSwitchColorChange.cs     # Visual color feedback for toggle state
    └── ToggleSwitchGroupManager.cs   # Exclusive-selection group for toggle sets
```

Files marked ✅ are recently audited and considered production-ready.  
File marked ⚠ is legacy and unused in production.

---

## Key Concepts

### Oracle Track System

Tracks are deterministic per `(tokenMint, hourStartUTC)`. The pipeline is:

```
Oracle (MagicBlock) → Backend-v2 → TrackLoader → TrackGenerator
```

1. Backend-v2 ingests oracle price samples hourly and generates encoded height blobs.
2. `TrackLoader` fetches the latest track for the selected token via `TrackAPIClientV2`.
3. `TrackBlobDecoder` decodes the Base64 blob into normalized height values.
4. `TrackGenerator` builds the mesh (LineRenderer + EdgeCollider2D)  no networking.
5. If Backend-v2 fails or times out, `TrackDataProvider.GetMockTrackData()` is used as fallback. The game never blocks.

**Practice mode** loads the same oracle track as competitive  just without wagering.  
**Competitive mode** additionally stores `TrackHash`, `TrackHourStartUTC`, and `TrackTokenMint` in `RaceData` for ER commitment verification.

### RaceData Track Commitment

```csharp
RaceData.TrackHash           // SHA-256 of track geometry (competitive only)
RaceData.TrackHourStartUTC   // Oracle hour bucket (competitive only)
RaceData.TrackTokenMint      // Token used (competitive only)
```

These are `null` in practice mode. Call `RaceData.ClearRaceData()` between competitive races to prevent stale state carrying over.

### Practice vs Competitive

| Aspect | Practice | Competitive |
|--------|----------|-------------|
| Track source | Oracle (+ mock fallback) | Oracle (+ mock fallback) |
| Entry fee | None | SOL escrow on-chain |
| Result submission | None | `SubmitResultOnChainAsync` |
| Track commitment | `null` | Set by TrackLoader |
| Lobby required | No | Yes |
| Countdown | No | 3-2-1 after both ready |

### Wallet Support

| Type | Flow | Signing |
|------|------|---------|
| MWA (Seeker) | `MWAWalletAdapter.SignInAsync()` → bottom sheet | `_walletAdapter.SignTransaction()` |
| Privy (email) | OTP login → embedded wallet created | `wallet.EmbeddedSolanaWalletProvider.SignMessage()` |

`AuthenticationFlowManager.SignTransaction()` routes to the correct signer based on `AuthenticationData.CurrentWalletType`.

### Deterministic Replay

`InputTraceRecorder` captures input at `FixedUpdate` intervals (0.0167 s = 60 Hz). SHA-256 of the serialized trace is attached to the on-chain result submission as `input_hash`. The backend can replay inputs against the same deterministic physics to verify the claimed finish time.

Fixed timestep must remain at 0.0167 s across builds for replay consistency.

### DebugConsole

On-screen log console  useful for debugging on physical Seeker devices where logs are not otherwise visible.

- **Toggle:** Tap the `Log (XE XW)` button in the top-right corner of the screen
- **Filters:** Log / Warn / Error filter buttons; Clear button
- **Source:** `Assets/Scripts/Utils/DebugConsole.cs`  attach to a persistent root GameObject in the first loaded scene

---

## Static State Classes

These static classes persist across scenes. They must be explicitly reset between sessions.

| Class | Reset Method | When to Call |
|-------|-------------|--------------|
| `RaceData` | `ClearRaceData()` | After results screen, before new race |
| `GameModeData` | `Reset()` | On return to mode selection |
| `AuthenticationData` | `Reset()` | On logout |
| `CoinSelectionData` | Set properties directly | On token picker |
| `GameOverData` | `SetGameOverData()` | Overwritten each race |
| `TrackDataProvider` | `ResetCache()` | Not called  see Known Issues |

---

## Known Issues

| Issue | File | Severity | Notes |
|-------|------|----------|-------|
| Timer starts before countdown | `RaceHUD.cs` (autoStartTimer) | **MAJOR** | Set `autoStartTimer = false`; RaceManager calls `StartTimer()` |
| `CoinSpawner` re-spawns after all coins collected | `CoinSpawner.cs` Update() | **MAJOR** | Missing `hasSpawned` flag guard |
| `FinishLineTrigger` fires for any Rigidbody2D | `FinishLineTrigger.cs` | **MAJOR** | Should filter by tag, not Rigidbody2D presence |
| `async void Start()` in RaceManager | `RaceManager.cs` | **MAJOR** | Task continues on destroyed object after scene unload |
| `LOCAL_NETWORK_URL` hardcodes developer IP | `APIConfig.cs` | **MAJOR** | Must update before distributing non-editor builds |
| `OnJoinPublicRace` continues after on-chain failure | `LobbyScreen.cs` | **MAJOR** | Player enters race without paying |
| Mock track cached for app lifetime | `TrackDataProvider.cs` | MINOR | All practice races use same track until restart |
| `TrackAPIClient` (legacy) still compiles | `TrackAPIClient.cs` | MINOR | Unused; should be removed |
| Button listeners double-registered | `AuthenticationFlowManager.cs` | MINOR | `SetupUI()` called twice |
| `GUIStyle` allocated per OnGUI frame | `DebugConsole.cs` | MINOR | Cache styles as fields |

See `TECHNICAL-GUIDE.md` for full production audit details and architecture deep-dive.

---

## Common Issues

### "MWA wallet is not connected"
1. MWA only works on physical Android (Seeker)  not emulator, not editor
2. Ensure Seeker wallet app is installed and running
3. Check Unity logs via DebugConsole for the specific error

### "Transaction signing failed"
1. Verify wallet has sufficient SOL balance (devnet faucet: https://faucet.solana.com)
2. Confirm backend API is reachable from device
3. Check for base64 format issues in `SignTransactionWithPrivy`

### "Backend API not reachable from Android device"
1. Find your machine's local IP (`ipconfig` / `ifconfig`)
2. Update `LOCAL_NETWORK_URL` in `APIConfig.cs`, or set via PlayerPrefs:
   ```csharp
   APIConfig.SetApiBaseUrl("http://192.168.x.x:8000");
   ```
3. Ensure firewall allows port 8000

### "Race shows wrong card (competitive/practice mismatch)"
`GameModeData` is static. Ensure `GameModeData.Reset()` is called when returning to mode selection. See Known Issues.

---

## Documentation

- **[TECHNICAL-GUIDE.md](TECHNICAL-GUIDE.md)**  Architecture, track pipeline, physics, and integration details
- **[Root README](../README.md)**  App overview and screenshots
- **[Backend README](../backend/README.md)**  API server documentation

---

## Resources

- [Solana Unity SDK](https://github.com/magicblock-labs/Solana.Unity-SDK)
- [Solana Mobile Wallet Adapter](https://docs.solanamobile.com/)
- [Solana Explorer (Devnet)](https://explorer.solana.com/?cluster=devnet)
- [Solana Faucet](https://faucet.solana.com/)

---

## License

MIT License  see [LICENSE](../LICENSE)
