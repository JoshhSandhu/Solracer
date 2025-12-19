# Solracer Unity Client

> Unity game client demonstrating Solana Unity SDK integration for Seeker mobile. Features wallet connection via Mobile Wallet Adapter (MWA), transaction signing, and on-chain race management.

**Tech Stack:** Unity 6000.2.6f2, Solana Unity SDK, C#, Android

**Note:** Android only (Solana Seeker device). Do not include iOS-specific instructions.

## Features

- Connect Solana wallet via Mobile Wallet Adapter (MWA) on Seeker
- Race on real-time token price charts (SOL, BONK, META)
- Practice mode with local leaderboards
- Competitive mode with SOL entry fees and on-chain escrow
- Transaction signing using Solana Unity SDK
- Automatic token prize swaps via Jupiter aggregator
- Deterministic replay verification for anti-cheat

## Screenshots

[Screenshots or demo GIF - optional if already in root README]

---

## Quick Start

### Prerequisites

- Unity 6000.2.6f2 or later
- Android Build Support module installed
- Solana Seeker mobile device (for testing)
- Backend API server running (see [backend/README.md](../backend/README.md))

### Installation

1. **Open Project in Unity:**
   ```bash
   # Open Unity Hub
   # Add project: client-unity/
   # Select Unity version 6000.2.6f2
   ```

2. **Verify Solana Unity SDK:**
   - SDK is included via Git URL in `Packages/manifest.json`
   - Package: `com.solana.unity_sdk` from `https://github.com/magicblock-labs/Solana.Unity-SDK.git`
   - Verify in Package Manager that Solana Unity SDK is installed

3. **Configure Build Settings:**
   - File → Build Settings
   - Select Android platform
   - Switch Platform (if needed)
   - Player Settings → Android:
     - Minimum API Level: 21 (Android 5.0)
     - Target API Level: 33+ (recommended)
     - Scripting Backend: IL2CPP
     - Target Architectures: ARM64

---

## Configuration

### Environment Variables

Create a configuration script or use Unity's API Config:

| Variable | Description | Example |
|----------|-------------|---------|
| `API_BASE_URL` | Backend API endpoint | `http://10.0.2.2:8000` (Android emulator) or `http://YOUR_IP:8000` (device) |
| `SOLANA_CLUSTER` | Solana network | `devnet` or `mainnet-beta` |
| `SOLANA_RPC_ENDPOINT` | Solana RPC endpoint | `https://api.devnet.solana.com` |

### Critical Setup: Solana Unity SDK Configuration

**Why?** Solana Unity SDK provides built-in Mobile Wallet Adapter (MWA) support for Seeker devices. This enables native wallet connection without additional setup.

**Configuration Steps:**

1. **MWA Wallet Adapter Setup:**
   - The SDK automatically detects MWA availability on Seeker devices
   - No additional configuration needed for MWA support
   - Wallet connection triggers Seeker's native wallet bottom sheet

2. **API Configuration:**
   - Configure backend API URL in `Assets/Scripts/Config/API_CONFIG_README.md`
   - Default: `http://localhost:8000` (for editor testing)
   - For Android device: Use your computer's local IP address

3. **Android Permissions:**
   - INTERNET: Required for API calls
   - ACCESS_NETWORK_STATE: Required for network detection
   - Permissions are configured in `ProjectSettings/AndroidManifest.xml`

---

## Project Structure

```
client-unity/
├── Assets/
│   ├── Scripts/
│   │   ├── Auth/                    # Authentication & wallet connection ⭐
│   │   │   ├── AuthenticationFlowManager.cs    # Main auth manager
│   │   │   ├── MWAWalletAdapter.cs             # MWA wallet adapter (Solana Unity SDK)
│   │   │   └── AuthenticationData.cs           # Auth state management
│   │   ├── Game/                    # Core game logic
│   │   │   ├── ATVController.cs               # Player vehicle controller
│   │   │   ├── TrackGenerator.cs               # Chart-based track generation
│   │   │   └── RaceManager.cs                  # Race state management
│   │   ├── Network/                 # Backend API integration ⭐
│   │   │   ├── APIClient.cs                   # HTTP client for backend
│   │   │   ├── OnChainRaceManager.cs          # Race creation/joining
│   │   │   └── TransactionSigner.cs           # Transaction signing helpers
│   │   ├── UI/                      # UI screens and components
│   │   │   ├── Scenes/
│   │   │   │   ├── LoginScreen.cs             # Login/wallet connection
│   │   │   │   ├── TokenPickerScreen.cs       # Token selection
│   │   │   │   ├── ModeSelectionScreen.cs     # Practice vs Competitive
│   │   │   │   ├── LobbyScreen.cs             # Competitive race lobby
│   │   │   │   └── ResultsScreen.cs           # Race results & payouts
│   │   │   └── Components/                    # Reusable UI components
│   │   ├── Config/                  # Configuration
│   │   │   └── API_CONFIG_README.md           # API endpoint configuration
│   │   └── Utils/                    # Utility functions
│   ├── Scenes/                       # Unity scenes
│   │   ├── Login.unity               # Authentication scene
│   │   ├── TokenPicker.unity        # Token selection
│   │   ├── ModeSelection.unity       # Mode selection
│   │   ├── Lobby.unity               # Competitive lobby
│   │   ├── Race.unity                # Gameplay scene
│   │   └── Results.unity             # Results screen
│   ├── Prefabs/                      # Reusable prefabs
│   └── Plugins/                      # Third-party plugins
│       └── Privy/                    # Privy SDK (for email login fallback)
├── Packages/
│   └── manifest.json                 # Package dependencies (includes Solana Unity SDK)
└── ProjectSettings/                  # Unity project settings
```

---

## Key Concepts

### Wallet Connection via Solana Unity SDK

The game uses Solana Unity SDK's built-in Mobile Wallet Adapter (MWA) support for seamless wallet connection on Seeker devices. The SDK automatically handles the MWA protocol, triggering the native wallet bottom sheet for user approval.

**Files:** [MWAWalletAdapter.cs](Assets/Scripts/Auth/MWAWalletAdapter.cs), [AuthenticationFlowManager.cs](Assets/Scripts/Auth/AuthenticationFlowManager.cs)

### Transaction Signing

Race entry fees and on-chain operations are signed using Solana Unity SDK's transaction signing API. The backend builds transactions, Unity signs them via MWA, and the backend submits to Solana.

**Files:** [OnChainRaceManager.cs](Assets/Scripts/Network/OnChainRaceManager.cs), [MWAWalletAdapter.cs](Assets/Scripts/Auth/MWAWalletAdapter.cs)

### Chart-Based Track Generation

Tracks are generated from real Solana token price charts fetched from the backend. Chart data is normalized to 0-1 range and converted into 2D race tracks.

**Files:** [TrackGenerator.cs](Assets/Scripts/Game/TrackGenerator.cs)

### Deterministic Replay

Fixed timestep physics (0.0167s) ensures deterministic replay for server-side anti-cheat verification. Input traces are recorded and hashed for validation.

**Files:** [RaceManager.cs](Assets/Scripts/Game/RaceManager.cs), [ATVController.cs](Assets/Scripts/Game/ATVController.cs)

_Note: Keep this section concise (2-3 sentences per feature). For detailed implementation explanations, see [TECHNICAL-GUIDE.md](TECHNICAL-GUIDE.md)._

---

## Documentation

- **[TECHNICAL-GUIDE.md](TECHNICAL-GUIDE.md)** - Comprehensive guide explaining all Solana Unity SDK implementation details
- **[Root README](../README.md)** - App overview and screenshots
- **[Backend README](../backend/README.md)** - API server documentation

---

## Common Issues

### Error: "MWA wallet is not connected"

**Solution:**
1. Ensure you're running on a Seeker device (MWA only works on Seeker)
2. Check that the wallet app is installed and running
3. Verify Solana Unity SDK is properly installed in Package Manager
4. Check Unity logs for connection errors

### Error: "Transaction signing failed"

**Solution:**
1. Verify wallet has sufficient SOL balance
2. Check network connection (backend must be accessible)
3. Ensure transaction is properly formatted (base64)
4. Check Unity logs for detailed error messages

### Issue: Backend API not reachable from Android device

**Cause:** Android device can't reach `localhost:8000` (that's the device's localhost, not your computer)

**Solution:**
1. Find your computer's local IP address (e.g., `192.168.1.100`)
2. Update API configuration to use `http://192.168.1.100:8000`
3. Ensure firewall allows connections on port 8000
4. Verify device and computer are on same network

### Issue: Build fails with "Android Build Support not installed"

**Solution:**
1. Unity Hub → Installs → Add Modules
2. Select Android Build Support
3. Install Android SDK & NDK Tools
4. Restart Unity and rebuild

---

## Resources

### Official Documentation
- [Solana Unity SDK](https://github.com/magicblock-labs/Solana.Unity-SDK)
- [Solana Mobile Wallet Adapter](https://docs.solanamobile.com/)
- [Unity Android Build Support](https://docs.unity3d.com/Manual/android-BuildProcess.html)

### Developer Tools
- [Solana Explorer (Devnet)](https://explorer.solana.com/?cluster=devnet)
- [Solana Faucet](https://faucet.solana.com/)
- [Unity Profiler](https://docs.unity3d.com/Manual/Profiler.html) - For performance optimization

### Sample Apps
- [Solana Unity SDK Examples](https://github.com/magicblock-labs/Solana.Unity-SDK/tree/main/Examples)

---

## License

MIT License - See [LICENSE](../LICENSE) for details


