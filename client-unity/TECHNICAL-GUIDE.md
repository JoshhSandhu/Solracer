# Solracer Unity — Technical Guide

> Deep-dive architecture, track pipeline, physics, and integration details.  
> For quick start, see [README.md](README.md).

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Unity Track Architecture](#2-unity-track-architecture)
3. [Race State Architecture](#3-race-state-architecture)
4. [Wallet & Auth Architecture](#4-wallet--auth-architecture)
5. [On-Chain Race Flow](#5-on-chain-race-flow)
6. [Physics Architecture](#6-physics-architecture)
7. [Input Trace & Replay Verification](#7-input-trace--replay-verification)
8. [UI Architecture](#8-ui-architecture)
9. [Static State Bus](#9-static-state-bus)
10. [Testing & Development](#10-testing--development)

---

## 1. Architecture Overview

### System Map

```
┌─────────────────────────────────────────────────────────┐
│                     SCENE FLOW                          │
│  Login → TokenPicker → ModeSelection → [Lobby] → Race → Results
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                   TRACK PIPELINE                        │
│                                                         │
│  Oracle (MagicBlock)                                    │
│       │ hourly price samples                            │
│       ▼                                                 │
│  Backend-v2 (api.lynxjosh.cyou)                         │
│       │ /tracks/latest, /tracks/detail                  │
│       │ Base64-encoded height blob + metadata           │
│       ▼                                                 │
│  TrackAPIClientV2  ──→  TrackBlobDecoder                │
│       │ float[] normalized heights                      │
│       ▼                                                 │
│  TrackLoader  ──→  LoadedTrackData                      │
│       │                                                 │
│       ▼  (mock fallback if offline/timeout)             │
│  TrackDataProvider.GetMockTrackData()                   │
│       │                                                 │
│       ▼                                                 │
│  TrackGenerator  ──→  LineRenderer + EdgeCollider2D     │
│                  ──→  TrackPoints (Vector2[])            │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                  ON-CHAIN RACE FLOW                     │
│                                                         │
│  LobbyScreen                                            │
│      │  create_race transaction                         │
│      ▼                                                  │
│  OnChainRaceManager ──→ TransactionAPIClient            │
│      │  build → sign → submit                           │
│      ▼                                                  │
│  Solana Program (race escrow)                           │
│      │  join_race                                       │
│      ▼                                                  │
│  RaceManager (both players ready)                       │
│      │  submit_result                                   │
│      ▼                                                  │
│  ResultsScreen                                          │
│      │  settle_race + claim_prize                       │
│      ▼                                                  │
│  PayoutAPIClient → Jupiter swap (non-SOL tokens)        │
└─────────────────────────────────────────────────────────┘
```

### Persistence Architecture

Several MonoBehaviours and static classes persist across scenes. Understanding their lifetimes is critical for ER multiplayer.

**DontDestroyOnLoad MonoBehaviours:**

| Component | Created By | When |
|-----------|-----------|------|
| `AuthenticationFlowManager` | Login scene | Once, on first load |
| `MWAWalletAdapter` | Lazy via `.Instance` | First access |
| `RaceAPIClient` | Lazy via `.Instance` | First access |
| `TransactionAPIClient` | Lazy via `.Instance` | First access |
| `PayoutAPIClient` | Lazy via `.Instance` | First access |
| `InputTraceRecorder` | RaceManager.Awake() | Race scene load |

**Static classes (live for application lifetime):**

`RaceData`, `GameModeData`, `GameOverData`, `CoinSelectionData`, `AuthenticationData`, `TrackDataProvider`

---

## 2. Unity Track Architecture

### TrackLoader Responsibilities

`TrackLoader` is the single entry point for all track data. It is the *only* system that calls `TrackAPIClientV2` or `TrackDataProvider`.

Responsibilities:
- Selects the track source (Backend-v2 primary, mock fallback)
- Calls `TrackAPIClientV2.GetLatestTrack(tokenMint)` or the detail endpoint
- Passes the decoded float[] to `TrackGenerator.SetTrackData()`
- Calls `TrackGenerator.GenerateTrackFromData(loadedData)`
- In **competitive mode**, writes to `RaceData`:
  - `RaceData.TrackHash` — SHA-256 of the track geometry blob
  - `RaceData.TrackHourStartUTC` — oracle hour bucket identifier
  - `RaceData.TrackTokenMint` — token used for this track
- Populates `LoadedTrackData` for downstream consumers

### TrackGenerator Responsibilities

`TrackGenerator` is **geometry-only**. It has no knowledge of networking, game mode, or backend state.

API surface:
- `SetTrackData(float[])` — stores normalized height data
- `GenerateTrackFromData(LoadedTrackData)` — builds LineRenderer + EdgeCollider2D + populates `TrackPoints`
- `TrackPoints` (Vector2[]) — read by `RaceManager`, `CoinSpawner`, `SupportResistanceManager`, `GhostCandleLayer`

TrackGenerator does **not** call the backend. It never reads `GameModeData` or `RaceData` directly.

> Note: `TrackGenerator` retains a `#region Legacy Methods` block with `[Obsolete]` methods that reference `TrackAPIClient` directly. These are no longer called but still compile. They should be removed before ER launch.

### LoadedTrackData Model

```csharp
public class LoadedTrackData
{
    public float[] Heights;           // Normalized 0-1 height values
    public string TokenMint;          // Token mint address
    public string HourStartUTC;       // Oracle hour bucket
    public string TrackHash;          // SHA-256 of blob
    public string Difficulty;         // "easy" | "medium" | "hard"
    public bool IsFromOracle;         // false if mock fallback
}
```

### Deterministic Tracks

Tracks are deterministic per `(tokenMint, hourStartUTC)`. Given the same inputs, the same float[] height array is always produced by Backend-v2, and the same geometry is always produced by TrackGenerator. This is the foundation for ER commitment verification.

### Difficulty Classification

Backend-v2 classifies tracks based on price volatility:
- `easy` — low volatility, smooth terrain
- `medium` — moderate hills
- `hard` — high volatility, sharp peaks

Stored in `LoadedTrackData.Difficulty`. Currently unused in gameplay balance but available for UI display and future difficulty gating.

### TrackHash Commitments

For competitive ER verification:

```
RaceData.TrackHash          ← SHA-256 of the Base64 blob from Backend-v2
RaceData.TrackHourStartUTC  ← e.g. "2026-03-02T14:00:00Z"
RaceData.TrackTokenMint     ← e.g. "DezXAZ8z7Pnrn..." (BONK)
```

These are `null` in practice mode. `RaceData.ClearRaceData()` resets all three.

> **ER Gap (Pre-launch):** The track commitment fields are written by TrackLoader, but `LobbyScreen` does not currently verify that both players committed to the same track hash before starting the race. This verification step must be added before ER multiplayer.

### Mock Fallback Logic

If Backend-v2 returns an error, times out, or returns an invalid blob:

1. TrackLoader logs a warning
2. Falls back to `TrackDataProvider.GetMockTrackData()` — sine-wave terrain
3. `LoadedTrackData.IsFromOracle = false`, `TrackHash = null`
4. Race proceeds normally — game never blocks

In competitive mode, a mock fallback means `RaceData.TrackHash = null`. The backend must handle this case by not requiring a track commitment for that race. This is an unresolved edge case.

### Retry and Caching

- `TrackAPIClientV2` caches `TrackDetailResponse` in-memory keyed by `"tokenMint:hourStartUTC"`
- Cache is bounded by realistic usage (~72 entries max per 3 tokens × 24 hours)
- One automatic retry after 2s on network failure
- No cache invalidation (intentional — same hour slot always yields same data)

---

## 3. Race State Architecture

### RaceData (Static)

`RaceData` is the cross-scene data store for an active race. It is never reset automatically — callers are responsible.

```csharp
// Active race identity
RaceData.CurrentRaceId          // On-chain race ID from backend
RaceData.CurrentRacePDA         // Program-Derived Address (unused currently)
RaceData.EntryFeeSol            // Entry fee amount

// Completion state
RaceData.HasFinishedRace        // Player crossed finish or game over
RaceData.ResultSubmittedOnChain // Submit transaction confirmed
RaceData.PlayerFinishTime       // Seconds
RaceData.PlayerCoinsCollected   // Count
RaceData.PlayerInputHash        // 64-char hex (SHA-256 of input trace)

// Track commitment (competitive only)
RaceData.TrackHash
RaceData.TrackHourStartUTC
RaceData.TrackTokenMint
```

**Critical:** Call `RaceData.ClearRaceData()` before each new competitive race to prevent stale fields carrying over. This is currently only done in `LobbyScreen.ResetCreateUI()` (cancel path). A normal race completion does not call `ClearRaceData()` — this should be fixed.

### RaceManager Lifecycle

```
Awake()
  ├── Find ATVController (by name "ATV" if null)
  ├── Find TrackGenerator
  ├── Find or Create InputTraceRecorder (DontDestroyOnLoad)
  └── SetupFinishLine()

async Start()
  ├── [Competitive] WaitForBothPlayersReady() — polls up to 60s
  │     └── StartCoroutine(CountdownCoroutine()) → StartRace()
  └── [Practice] StartRace() immediately

Update()
  ├── [if autoCreateFinishLine] CreateFinishLine() when TrackPoints available
  ├── CheckUpsideDown() → RespawnPlayer() after 10s upside-down
  └── CheckIfStuck() → RespawnPlayer() after 3s stuck

OnFinishLineCrossed() → TriggerRaceComplete()
TriggerRaceComplete() / TriggerGameOver()
  ├── Stop InputTraceRecorder
  ├── Get time from RaceHUD, speed from ATVController
  ├── SaveCollectedCoins via CoinCollectionManager
  ├── CalculateInputHash
  ├── RaceData.SetRaceFinished(time, coins, hash)
  ├── [Competitive] SubmitResultOnChainWithResult(...)
  └── LoadScene("Results")
```

### GameModeData (Static)

Simple enum wrapper. Defaults to `Practice`. Set to `Competitive` by `ModeSelectionScreen` or `LobbyScreen`.

**Known issue:** `GameModeData.Reset()` is defined but not called on return to menu from competitive mode. This can cause `ResultsScreen` to display the competitive card during the next practice run. Fix: call `GameModeData.Reset()` in `ModeSelectionScreen.Start()`.

---

## 4. Wallet & Auth Architecture

### Dual Wallet Support

```
┌─────────────────────────────────────────────────────┐
│           AuthenticationFlowManager                  │
│                                                      │
│  WalletType.MWA ──→ MWAWalletAdapter                 │
│       SignTransaction → SolanaWalletAdapter.SignTransaction()
│                                                      │
│  WalletType.Privy ──→ EmbeddedSolanaWalletProvider   │
│       SignTransaction → wallet.SignMessage(base64)   │
│       (signs message bytes, not full transaction)    │
└─────────────────────────────────────────────────────┘
```

`AuthenticationFlowManager.SignTransaction(base64)` routes to the appropriate path based on `AuthenticationData.CurrentWalletType`.

### Privy Transaction Signing — Critical Detail

Privy's embedded wallet API does not expose a `SignTransaction` method — it only exposes `SignMessage`. Solracer works around this by:

1. Deserializing the transaction (Solana Unity SDK `Transaction.Deserialize`)
2. Compiling the message bytes (`transaction.CompileMessage()`)
3. Signing the message bytes via `wallet.EmbeddedSolanaWalletProvider.SignMessage(base64)`
4. Injecting the signature back into the transaction's `Signatures` list
5. Re-serializing and returning base64

This is a non-standard flow. If Solana transaction structure changes (e.g., versioned transactions), this path may break.

**Known risk:** If `transaction.Signatures[0].PublicKey` does not match `WalletAddress`, the signature is force-placed into slot 0. This is incorrect for multi-signer transactions.

### MWA Transaction Signing

MWA signing is handled entirely by Solana Unity SDK:
- `_walletAdapter.SignTransaction(transaction)` triggers the Seeker wallet bottom sheet
- Returns a fully signed `Transaction` object
- `MWAWalletAdapter` serializes and returns base64

MWA is only available on physical Android devices. On any other platform, `ConnectAsync()` returns an error immediately.

### Auth State Persistence

`AuthenticationFlowManager` uses `DontDestroyOnLoad`. Its UI panel references (`authPanel`, `emailLoginPanel`, etc.) point to objects in the **Login scene**. When the Login scene unloads, these references become invalid.

`HideAllPanels()` has null checks and will not throw, but it will silently fail to hide panels that were destroyed with the scene. The panels are re-wired when the Login scene reloads via `SetAuthPanelReferences()`.

---

## 5. On-Chain Race Flow

### Create Race (Player 1)

```
LobbyScreen.OnCreateGameClicked()
  → OnChainRaceManager.CreateRaceOnChainAsync(tokenMint, entryFee)
      → TransactionAPIClient.BuildTransactionAsync({ instruction_type: "create_race" })
      → ShowTransactionSigningModal() [Privy only; MWA shows bottom sheet natively]
      → AuthenticationFlowManager.SignTransaction(transaction_bytes)
      → TransactionAPIClient.SubmitTransactionAsync({ instruction_type: "create_race" })
      ← returns race_id (on-chain race PDA identifier)
  → RaceData.CurrentRaceId = race_id
  → StartStatusPolling() — polls GET /races/{id}/status every 2s
```

### Join Race (Player 2)

```
LobbyScreen.OnJoinByCodeClicked() or OnJoinPublicRace()
  → RaceAPIClient.JoinRaceByCodeAsync() or JoinRaceByIdAsync()
      ← RaceResponse { race_id, entry_fee_sol, ... }
  → OnChainRaceManager.JoinRaceOnChainAsync(race_id)
      → BuildTransactionAsync({ instruction_type: "join_race" })
      → SignTransaction
      → SubmitTransactionAsync({ instruction_type: "join_race" })
  ⚠ If JoinOnChain fails, code logs warning but continues
  → RaceData.CurrentRaceId = race_id
  → StartStatusPolling()
```

**Known risk:** If `JoinRaceOnChainAsync` returns false, the join is not aborted. The player can enter the race without having paid the entry fee on-chain. This must be fixed before ER launch.

### Submit Result

```
RaceManager.TriggerRaceComplete() or TriggerGameOver()
  → OnChainRaceManager.SubmitResultOnChainAsync(raceId, finishTimeMs, coinsCollected, inputHash)
      → BuildTransactionAsync({ instruction_type: "submit_result", finish_time_ms, coins_collected, input_hash })
      → ShowTransactionSigningModal()
      → SignTransaction
      → SubmitTransactionAsync({ instruction_type: "submit_result", wallet_address, finish_time_ms, ... })
  → RaceData.SetResultSubmitted(success)
```

If submission fails (rejected by user, network error), `RaceData.NeedsResultSubmission()` returns `true`. `ResultsScreen` detects this and offers a retry button (`fallbackTxnButton`).

### Settle & Claim Prize

```
ResultsScreen.OnClaimPrizeClicked()
  → PayoutAPIClient.GetSettleTransaction(raceId, walletAddress)
      → Signs and submits settle_race transaction (if needed)
  → [SOL token] OnChainRaceManager.ClaimPrizeOnChainAsync(raceId)
  → [Non-SOL token] ResultsScreen.HandleJupiterSwap(raceId)
      → PayoutAPIClient.ProcessPayout(raceId) ← returns Jupiter swap transaction
      → SignTransaction
      → TransactionAPIClient.SubmitTransactionAsync({ instruction_type: "jupiter_swap" })
```

---

## 6. Physics Architecture

### ATVController Overview

The ATV uses a torque-driven wheel physics model — both tires have independent `Rigidbody2D` components connected to the ATV body via physics joints (configured in scene prefab). Input applies torque to both tires simultaneously, which propels the body forward via friction.

```
ATVController
├── Rigidbody2D (body) — Continuous collision detection, linear/angular damping
├── frontTire Rigidbody2D — default settings, torque applied
├── backTire Rigidbody2D  — default settings, torque applied
└── InputSystem (BikeControler asset) — Accelerate / Brake / Rotate actions
```

### Tire Tunneling Risk

The ATV body `Rigidbody2D` is configured with `CollisionDetectionMode2D.Continuous` in `ConfigureRigidbody()`. **The tire Rigidbodies are not configured in code** — they retain their Inspector defaults, typically `Discrete`.

At `maxSpeed = 20f` units/second, thin track section colliders (~0.1–0.2 units wide) can be tunneled through by a tire using Discrete detection in a single physics step. This is the primary cause of tires clipping through the track.

**Why:** Discrete detection only checks collision at the end of each physics step. If a tire moves far enough in one step to skip past a thin collider, no collision is registered. Continuous detection resolves the intermediate path instead.

### Ground Check Fragility

`IsGrounded()` fires three raycasts downward with `groundLayerMask = -1` (all layers). The `= -1` default means the ATV's own collider is included in the cast. This can return `true` when the ATV is not grounded because the ray hits part of the ATV's own body collider below the origin point.

Additionally, the raycast direction is always `Vector2.down` in world space. When the ATV is rotated mid-air (e.g., flipping), "down" does not track the ATV's local orientation. A flipped ATV pointing wheels upward still fires raycasts in world-down direction, which may miss the terrain.

**Why it matters:** `ApplyMidAirRotation()` checks `IsGrounded()` before applying torque. False-positive grounding (from the ATV's own collider or wrong direction) suppresses mid-air rotation control. False-negative (missed terrain) applies unwanted torque while on the ground, destabilizing the vehicle.

### Mid-Air Rotation — Shared Input

The accelerate input (`currentAccelerateInput`) is used for both tire torque and mid-air rotation simultaneously. There is no dedicated "lean" input binding.

When the ATV leaves the ground:
- `ApplyTireTorque()` still runs and applies torque to tires (wheels spin in air)
- `ApplyMidAirRotation()` also runs and applies body torque

The combined angular momentum can cause the ATV to over-rotate mid-air. On landing with high angular velocity, the suspension joints can receive impulses that destabilize the vehicle or flip it.

**Why:** Hill Climb Racing-style controls intentionally use accelerate/brake for mid-air lean. The problem is that there is no `maxAngularVelocity` cap on the ATV *body* Rigidbody2D (only on the tires). Uncapped body angular velocity allows indefinite spin-up during prolonged airtime.

### Speed Cap Implementation

`LimitMaxSpeed()` hard-clamps `atvRigidbody.linearVelocity = velocity.normalized * maxSpeed` every FixedUpdate.

This discontinuously overrides the velocity vector each physics frame when at max speed. It prevents smooth physics-based deceleration and creates an "invisible wall" feeling. More importantly, it overrides the velocity that the physics engine computed based on tire friction — this can create frame-to-frame velocity discontinuities that the constraint solver (joints) must compensate for, introducing jitter.

### No BikeController.cs

The audit tasked this file for review, but `BikeController.cs` does not exist in the project. The vehicle is controlled entirely by `ATVController.cs`. The input asset is named `BikeControler` (typo — missing second 'l'). This is a naming artifact from an earlier prototype.

---

## 7. Input Trace & Replay Verification

`InputTraceRecorder` records one `InputFrame` per FixedUpdate interval (0.0167s = 60 Hz):

```csharp
public class InputFrame
{
    public float time;       // Time.fixedTime
    public float accelerate; // 0.0–1.0
    public float brake;      // 0.0–1.0
    public float rotate;     // –1.0–1.0
}
```

The full trace is serialized to JSON and SHA-256 hashed:
```
inputHash = SHA256(JsonConvert.SerializeObject(inputTrace))
```

This hash is submitted on-chain as `input_hash` in the `submit_result` transaction. The backend server can replay the input trace against the deterministic physics engine to verify the claimed `finish_time_ms`.

### Current Limitations

1. **Timer does not reset when recording starts.** `InputFrame.time` uses `Time.fixedTime` — the absolute time since game start. If the race scene was loaded 5 seconds in, all timestamps are offset by 5. For replay, the backend must apply the same offset.

2. **Mobile input is not recorded.** `MobileInputControls` calls `SetUIAccelerateInput()` / `SetUIBrakeInput()` on `ATVController`. These UI inputs are combined with keyboard in `ATVController.Update()`. `InputTraceRecorder` reads directly from the Input System action, not from the combined value. Mobile-only input is lost from the trace.

3. **Recorder object persists with DontDestroyOnLoad** but is not reset between races. `StartRecording()` calls `inputTrace.Clear()`, so data is correct per-race. However, the recorder accumulates in the DDOL scene — on repeated plays, old recorders from previous races are not destroyed (though only the one found first by `FindAnyObjectByType` is used).

---

## 8. UI Architecture

### Scene → Screen Mapping

| Scene | Primary Screen | Notes |
|-------|---------------|-------|
| Login | `LoginScreen` | Wires panel refs to `AuthenticationFlowManager` |
| TokenPicker | `TokenPickerScreen` | Sets `CoinSelectionData.SelectedCoin` |
| ModeSelection | `ModeSelectionScreen` | Sets `GameModeData.CurrentMode` |
| Lobby | `LobbyScreen` | Create/join; on-chain tx; polling |
| Race | `RaceHUD` + `RaceManager` | Gameplay |
| Results | `ResultsScreen` | Results + prize claim |

### AuthenticationFlowManager and UI Coupling

`AuthenticationFlowManager` is a DontDestroyOnLoad MonoBehaviour that owns both auth logic **and** UI panel references. When the Login scene unloads, all 9 panel/button/text references become null. They are re-wired via `SetAuthPanelReferences()` called from `LoginScreen.Start()`.

This means: if any code calls `ShowWelcomePanel()`, `HideAllPanels()`, etc. while not in the Login scene, they silently do nothing. This is a design-level smell — auth logic and scene-specific UI should not live in the same class.

### LobbyScreen — Async/Coroutine Mixed Model

`LobbyScreen` uses two concurrent polling systems:
- `statusPollCoroutine` — calls `PollRaceStatus()` (async void) on a timer
- `publicRacesRefreshCoroutine` — calls `RefreshPublicRaces()` (async void) on a timer

`PollRaceStatus()` is `async void` called from a coroutine. If multiple calls overlap (e.g., ready button fires while coroutine is mid-poll), state updates can race. The winner/loser indicator can flicker or be set incorrectly.

### ResultsScreen — Polling Architecture

Two polling coroutines may run simultaneously:
- `raceStatusPollingCoroutine` — polls `GetRaceStatusAsync` every 2.5s
- `payoutPollingCoroutine` — polls `GetPayoutStatus` every 3s

The race status polling is supposed to stop when payout data is available. This logic is in `LoadPayoutStatus()` and `PollRaceStatus()`. If the stop-polling call is missed in an edge case, both coroutines run indefinitely until scene destruction.

---

## 9. Static State Bus

Six static C# classes act as the global state bus between scenes. None use events or notifications — consumers poll or read directly.

### Reset Responsibility Map

| State | Set By | Reset By | Missing Reset |
|-------|--------|---------|---------------|
| `RaceData` | `LobbyScreen`, `TrackLoader`, `RaceManager` | `LobbyScreen.ResetCreateUI()` (cancel only) | ✅ Not reset on normal race completion |
| `GameModeData` | `ModeSelectionScreen`, `LobbyScreen` | `GameModeData.Reset()` (not called) | ✅ Never reset after competitive race |
| `GameOverData` | `RaceManager.TriggerGameOver/Complete` | Overwritten each call | OK |
| `CoinSelectionData` | `TokenPickerScreen` | Not explicitly reset | OK — overwritten per session |
| `AuthenticationData` | `AuthenticationFlowManager` | `Reset()` on logout | OK |
| `TrackDataProvider` | Static initializer | `ResetCache()` — never called | ✅ Same mock track for entire session |

---

## 10. Testing & Development

### DebugConsole

Attach `DebugConsole.cs` to a persistent root GameObject (first scene). The console captures all `Debug.Log/Warning/Error` messages and displays them as an on-screen overlay. Useful on Seeker where ADB logs are not always accessible.

Toggle via the `Log (XE XW)` button in the top-right corner. Filter by type. Clear button resets counters.

> `DebugConsole.OnGUI` creates two `GUIStyle` objects (`activeStyle`, `inactiveStyle`) that are allocated but never read. These allocate every frame when the console is visible. Cache them as fields to eliminate GC pressure.

### API URL Override at Runtime

```csharp
// Override API URL without rebuilding
APIConfig.SetApiBaseUrl("https://staging-api.solracer.com");

// Clear override (revert to build-time default)
APIConfig.ClearApiBaseUrl();
```

The override is stored in `PlayerPrefs` and survives app restarts until cleared.

### Practice Mode Track Testing

To force mock track data (bypass Backend-v2):
- Disconnect from network, or
- Set an invalid API URL via `SetApiBaseUrl("http://invalid")`

`TrackLoader` will retry once after 2s, then fall back to `TrackDataProvider.GetMockTrackData()`.

To force a fresh mock track:
```csharp
TrackDataProvider.ResetCache();
```

Note: `ResetCache()` is never called by game code. Mock data is cached for the entire application session. All practice races (without Backend-v2) use the same terrain.

### Scripting Define Symbols

Set in **Player Settings → Other Settings → Scripting Define Symbols**:

| Symbol | Effect |
|--------|--------|
| `DEVELOPMENT_BUILD` | `APIConfig` uses `https://localhost:8000` |
| `STAGING_BUILD` | `APIConfig` uses `https://staging-api.solracer.com` |
| `PRODUCTION_BUILD` | `APIConfig` uses `https://api.solracer.com` |
| (none) | Unity Editor → localhost; Device → `LOCAL_NETWORK_URL` (hardcoded IP) |

### Input Asset Naming Note

The Input Actions asset is named `BikeControler` (intentional historical typo — missing second 'l'). `ATVController.InitializeInput()` searches for it by name:
```csharp
inputActionsAsset = Resources.FindObjectsOfTypeAll<InputActionAsset>()
    .FirstOrDefault(asset => asset.name == "BikeControler");
```
Do not rename the asset without updating this string.

---

*Last updated: 2026-03-02. Covers Unity client commit state as of the ER pre-launch production audit.*
