# Solana Unity SDK Integration Guide - Solracer

> **📖 Deep Dive Documentation**
> This is a comprehensive technical guide explaining all Solana Unity SDK integration details.
> For quick start instructions, see [README.md](README.md).

This document explains all the Solana Unity SDK integration steps implemented in the Solracer game, with detailed explanations of **why** each decision was made.

## Table of Contents
1. [Overview](#overview)
2. [Project Structure](#project-structure)
3. [Setup & Prerequisites](#setup--prerequisites)
4. [Solana Unity SDK Integration](#solana-unity-sdk-integration)
5. [Mobile Wallet Adapter (MWA)](#mobile-wallet-adapter-mwa)
6. [Wallet Connection](#wallet-connection)
7. [Transaction Signing](#transaction-signing)
8. [On-Chain Race Operations](#on-chain-race-operations)
9. [Error Handling](#error-handling)
10. [Testing & Development](#testing--development)

---

## Overview

Solracer is a fast-paced racing game where players race on Solana token price charts. The game integrates with Solana blockchain for:
- Wallet connection via Mobile Wallet Adapter (MWA) on Seeker devices
- Transaction signing for race entry fees and on-chain operations
- On-chain race escrow via Solana program
- Automatic token prize distribution

This guide explains how Solana Unity SDK enables these features in Unity/C#.

---

## Project Structure

The Unity client follows a clean, organized structure optimized for Solana Unity SDK integration:

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
│   │   │   └── TransactionAPIClient.cs        # Transaction building/submission
│   │   ├── UI/                      # UI screens and components
│   │   │   ├── Scenes/
│   │   │   │   ├── LoginScreen.cs             # Login/wallet connection
│   │   │   │   ├── TokenPickerScreen.cs       # Token selection
│   │   │   │   ├── ModeSelectionScreen.cs     # Practice vs Competitive
│   │   │   │   ├── LobbyScreen.cs             # Competitive race lobby
│   │   │   │   └── ResultsScreen.cs           # Race results & payouts
│   │   │   └── Components/                    # Reusable UI components
│   │   └── Utils/                    # Utility functions
│   ├── Scenes/                       # Unity scenes
│   └── Plugins/                      # Third-party plugins
│       └── Privy/                    # Privy SDK (for email login fallback)
├── Packages/
│   └── manifest.json                 # Package dependencies (includes Solana Unity SDK)
└── ProjectSettings/                  # Unity project settings
```

### Key Directories for Solana Integration

- **[Assets/Scripts/Auth/](Assets/Scripts/Auth/)**: Authentication & wallet connection ⭐
  - [MWAWalletAdapter.cs](Assets/Scripts/Auth/MWAWalletAdapter.cs): MWA wallet adapter using Solana Unity SDK
  - [AuthenticationFlowManager.cs](Assets/Scripts/Auth/AuthenticationFlowManager.cs): Main auth manager routing to MWA or Privy
  - [AuthenticationData.cs](Assets/Scripts/Auth/AuthenticationData.cs): Auth state management

- **[Assets/Scripts/Network/](Assets/Scripts/Network/)**: Backend API integration ⭐
  - [OnChainRaceManager.cs](Assets/Scripts/Network/OnChainRaceManager.cs): Race creation/joining with transaction signing
  - [TransactionAPIClient.cs](Assets/Scripts/Network/TransactionAPIClient.cs): Transaction building/submission API client

- **[Packages/manifest.json](Packages/manifest.json)**: Solana Unity SDK package configuration

---

## Setup & Prerequisites

### Required Dependencies

**Unity Package Manager** (`Packages/manifest.json`):
```json
{
  "dependencies": {
    "com.solana.unity_sdk": "https://github.com/magicblock-labs/Solana.Unity-SDK.git"
  }
}
```

**Why Solana Unity SDK?**
- Built-in Mobile Wallet Adapter (MWA) support for Seeker devices
- Native Android integration via MWA protocol
- No additional native plugins required
- Automatic wallet bottom sheet UI on Seeker

### Critical: Unity Version & Android Build Support

**Unity Version:** 6000.2.6f2 or later

**Why?**
- Solana Unity SDK requires Unity 6000+ for modern C# async/await support
- Android Build Support module must be installed
- IL2CPP scripting backend required for native interop

**Android Build Settings:**
- Minimum API Level: 21 (Android 5.0)
- Target API Level: 33+ (recommended)
- Scripting Backend: IL2CPP
- Target Architectures: ARM64

**Why IL2CPP?**
- Required for native Android interop with MWA
- Better performance than Mono
- Required for Solana Unity SDK's native Android components

### Verify Setup

Add this test to confirm Solana Unity SDK is working:

```csharp
// In any MonoBehaviour
using Solana.Unity.SDK;

void Start()
{
    Debug.Log("Solana Unity SDK available: " + (SolanaWalletAdapter != null));
}
```

**Expected Output:**
```
Solana Unity SDK available: True
```

---

## Solana Unity SDK Integration

### Overview

Solana Unity SDK provides a unified API for Solana operations in Unity. It includes:
- `SolanaWalletAdapter`: Main wallet adapter class
- `SolanaMobileWalletAdapterOptions`: MWA configuration
- Built-in MWA protocol support for Seeker devices

### Package Installation

The SDK is installed via Git URL in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.solana.unity_sdk": "https://github.com/magicblock-labs/Solana.Unity-SDK.git"
  }
}
```

**Why Git URL?**
- SDK is actively developed on GitHub
- Easy to update to latest version
- No manual .unitypackage installation needed

**Verify Installation:**
1. Window → Package Manager
2. Check "Solana Unity SDK" is listed
3. Verify version matches GitHub repository

---

## Mobile Wallet Adapter (MWA)

### Overview

Mobile Wallet Adapter (MWA) is a protocol that enables Unity games to connect to Solana wallets on mobile devices. On Seeker devices, MWA provides native wallet integration via the device's built-in wallet.

### Why MWA?

**1. Native Integration**
- Seeker devices have built-in wallet support
- No need for users to install separate wallet apps
- Seamless user experience

**2. Security**
- Private keys never leave the device
- Wallet approval UI is handled by the system
- No wallet app switching required

**3. Solana Unity SDK Support**
- SDK includes built-in MWA support
- No additional native plugins needed
- Automatic protocol handling

### MWA Wallet Adapter Implementation

The game uses `MWAWalletAdapter.cs` to wrap Solana Unity SDK's MWA functionality:

```csharp
// Assets/Scripts/Auth/MWAWalletAdapter.cs
using Solana.Unity.SDK;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Wallet;

public class MWAWalletAdapter : MonoBehaviour
{
    private SolanaWalletAdapter _walletAdapter;
    private Account _connectedAccount;
    
    private void InitializeWalletAdapter()
    {
        var options = new SolanaWalletAdapterOptions
        {
            solanaMobileWalletAdapterOptions = new SolanaMobileWalletAdapterOptions
            {
                identityUri = appIdentityUri,
                iconUri = appIconUri,
                name = appIdentityName,
                keepConnectionAlive = keepConnectionAlive
            }
        };
        
        _walletAdapter = new SolanaWalletAdapter(
            options,
            rpcCluster,
            rpcUri,
            null,
            false
        );
    }
}
```

**Why This Implementation?**

**1. Singleton Pattern**
- Single instance across scenes
- Persistent connection state
- `DontDestroyOnLoad` ensures connection survives scene changes

**2. Lazy Initialization**
- Wallet adapter only created when needed
- Reduces startup time
- Allows configuration before initialization

**3. App Identity Configuration**
- `identityUri`, `iconUri`, `name`: Shown in wallet approval UI
- Builds trust with users
- Required by MWA protocol

**4. RPC Cluster Configuration**
- `rpcCluster`: devnet, testnet, or mainnet-beta
- `customRpcUri`: Optional custom RPC endpoint
- Allows switching between networks

---

## Wallet Connection

### Implementation: [MWAWalletAdapter.cs](Assets/Scripts/Auth/MWAWalletAdapter.cs)

```csharp
public async Task<MWAConnectionResult> ConnectAsync()
{
    InitializeWalletAdapter();
    
    // Login calls the MWA authorize flow
    _connectedAccount = await _walletAdapter.Login();
    
    if (_connectedAccount != null && _connectedAccount.PublicKey != null)
    {
        _isConnected = true;
        string walletAddress = _connectedAccount.PublicKey.ToString();
        return new MWAConnectionResult
        {
            Success = true,
            WalletAddress = walletAddress,
            PublicKey = _connectedAccount.PublicKey.KeyBytes
        };
    }
}
```

### Why This Approach?

#### 1. **Using `Login()` Method**
**What**: `_walletAdapter.Login()` triggers MWA authorization flow
**Why**:
- SDK's `Login()` method handles full MWA authorization protocol
- Automatically opens Seeker's wallet bottom sheet
- Returns `Account` object with public key and signing capability
- No manual protocol handling needed

#### 2. **Android-Only Check**
**What**: `#if !UNITY_ANDROID` preprocessor directive
**Why**:
- MWA only works on Android devices (Seeker)
- Prevents errors in Unity Editor or other platforms
- Provides clear error message for developers

```csharp
#if !UNITY_ANDROID
    Debug.LogError("[MWAWalletAdapter] MWA is only supported on Android devices");
    return new MWAConnectionResult
    {
        Success = false,
        ErrorMessage = "MWA is only supported on Android devices."
    };
#endif
```

#### 3. **Account Object Storage**
**What**: Store `_connectedAccount` for later use
**Why**:
- `Account` object contains public key and signing methods
- Needed for transaction signing
- Persists connection state across scenes

#### 4. **Connection State Management**
**What**: `_isConnected` flag tracks connection status
**Why**:
- Prevents multiple connection attempts
- Allows UI to show connection status
- Enables graceful error handling

---

## Transaction Signing

### Implementation: [MWAWalletAdapter.cs](Assets/Scripts/Auth/MWAWalletAdapter.cs)

```csharp
public async Task<string> SignTransactionAsync(string transactionBase64)
{
    // Step 1: Decode base64 to bytes
    byte[] transactionBytes = Convert.FromBase64String(transactionBase64);
    
    // Step 2: Deserialize transaction
    Transaction transaction = Transaction.Deserialize(transactionBytes);
    
    // Step 3: Sign transaction using MWA SDK - triggers wallet bottom sheet
    Transaction signedTransaction = await _walletAdapter.SignTransaction(transaction);
    
    // Step 4: Serialize the signed transaction
    byte[] signedTransactionBytes = signedTransaction.Serialize();
    
    // Step 5: Return base64-encoded signed transaction
    string signedTransactionBase64 = Convert.ToBase64String(signedTransactionBytes);
    return signedTransactionBase64;
}
```

### Why This Flow?

#### 1. **Backend Builds, Unity Signs**
**What**: Backend builds transaction, Unity only signs
**Why**:
- Backend has access to Solana program IDL and account structures
- Unity doesn't need full Solana SDK (only signing)
- Reduces Unity build size
- Centralizes transaction logic

**Flow:**
1. Unity requests transaction from backend: `POST /api/v1/transactions/build`
2. Backend builds transaction and returns base64-encoded bytes
3. Unity deserializes, signs via MWA, serializes
4. Unity submits signed transaction: `POST /api/v1/transactions/submit`

#### 2. **Base64 Encoding**
**What**: Transactions passed as base64 strings
**Why**:
- Easy to serialize/deserialize over HTTP
- No binary data issues in JSON
- Standard format for transaction bytes

#### 3. **Transaction Deserialization**
**What**: `Transaction.Deserialize(transactionBytes)`
**Why**:
- Solana Unity SDK's `Transaction` class handles deserialization
- Validates transaction structure
- Provides typed access to transaction fields

#### 4. **MWA Signing Triggers Bottom Sheet**
**What**: `await _walletAdapter.SignTransaction(transaction)`
**Why**:
- SDK automatically triggers Seeker's wallet bottom sheet
- User sees transaction details and approves/rejects
- No manual UI implementation needed
- Secure: private keys never exposed to Unity

#### 5. **Signature Verification**
**What**: Check signed transaction size (not just 64 bytes)
**Why**:
- 64 bytes = just a signature (wrong)
- Full transaction = signed transaction with all instructions
- Prevents returning only signature instead of full transaction

```csharp
if (signedTransactionBytes.Length == 64)
{
    Debug.LogError("ERROR: Serialized transaction is only 64 bytes - this is just a signature!");
    return null;
}
```

---

## On-Chain Race Operations

### Implementation: [OnChainRaceManager.cs](Assets/Scripts/Network/OnChainRaceManager.cs)

The game uses `OnChainRaceManager` to coordinate race creation/joining with on-chain operations:

```csharp
public static async Task<string> CreateRaceOnChainAsync(
    string tokenMint,
    float entryFeeSol,
    Action<string, float> onProgress = null)
{
    // 1. Build transaction (backend)
    var buildRequest = new BuildTransactionRequest
    {
        instruction_type = "create_race",
        wallet_address = walletAddress,
        token_mint = tokenMint,
        entry_fee_sol = entryFeeSol
    };
    var buildResponse = await apiClient.BuildTransactionAsync(buildRequest);
    
    // 2. Sign transaction (Unity via MWA)
    string signedTransaction = await authManager.SignTransaction(
        buildResponse.transaction_bytes
    );
    
    // 3. Submit transaction (backend)
    var submitRequest = new SubmitTransactionRequest
    {
        signed_transaction_bytes = signedTransaction,
        instruction_type = "create_race",
        race_id = buildResponse.race_id
    };
    var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
    
    return submitResponse.race_id;
}
```

### Why This Architecture?

#### 1. **Separation of Concerns**
**What**: Backend builds, Unity signs, backend submits
**Why**:
- Backend handles complex Solana program interaction
- Unity only needs signing capability (via MWA)
- Reduces Unity code complexity
- Centralizes transaction logic

#### 2. **Progress Callbacks**
**What**: `Action<string, float> onProgress` parameter
**Why**:
- Transaction signing can take time (user approval)
- UI needs to show progress to user
- Better UX than silent waiting

#### 3. **Transaction Signing Modal**
**What**: Show modal before signing
**Why**:
- User needs context about what they're signing
- Prevents accidental approvals
- Better security UX

#### 4. **Error Handling**
**What**: Try-catch blocks at each step
**Why**:
- Network errors can occur at any step
- User rejection during signing
- Transaction submission failures
- Graceful error messages for users

---

## Error Handling

### Common Errors & Solutions

#### Error: "MWA wallet is not connected"

**Cause**: `_isConnected` is false or `_walletAdapter` is null

**Solution:**
```csharp
if (!_isConnected || _walletAdapter == null)
{
    Debug.LogError("[MWAWalletAdapter] Not connected to MWA wallet");
    return null;
}
```

**Why It Happens:**
- User hasn't connected wallet yet
- Connection was lost (app backgrounded)
- Wallet app was closed

**Prevention:**
- Check connection before operations
- Reconnect if connection lost
- Show clear error messages to user

#### Error: "Transaction signing returned null"

**Cause**: User rejected transaction or wallet error

**Solution:**
```csharp
if (signedTransaction == null)
{
    Debug.LogError("[MWAWalletAdapter] SignTransaction returned null");
    // User rejected or wallet error
    return null;
}
```

**Why It Happens:**
- User tapped "Reject" in wallet bottom sheet
- Wallet app crashed or closed
- Network error during signing

**Prevention:**
- Show clear transaction details before signing
- Handle user rejection gracefully
- Retry on network errors

#### Error: "Serialized transaction is only 64 bytes"

**Cause**: SDK returned signature instead of full transaction

**Solution:**
```csharp
if (signedTransactionBytes.Length == 64)
{
    Debug.LogError("ERROR: This is just a signature, not a full transaction!");
    return null;
}
```

**Why It Happens:**
- SDK bug or version mismatch
- Incorrect transaction structure
- Wallet returned only signature

**Prevention:**
- Verify SDK version
- Check transaction structure before signing
- Validate signed transaction size

---

## Testing & Development

### Testing on Seeker Device

**1. Build for Android:**
- File → Build Settings → Android
- Switch Platform (if needed)
- Build and Run

**2. Enable Developer Options on Seeker:**
- Settings → About Phone → Tap Build Number 7 times
- Enable USB Debugging

**3. Connect Device:**
- Connect via USB
- Unity should detect device
- Run on device

**4. Test Wallet Connection:**
- Launch game
- Tap "Connect Wallet"
- Verify Seeker wallet bottom sheet appears
- Approve connection
- Verify wallet address displayed

**5. Test Transaction Signing:**
- Create or join a race
- Verify transaction signing modal appears
- Approve transaction in wallet
- Verify transaction submitted successfully

### Debugging Tips

**1. Enable Detailed Logging:**
```csharp
[SerializeField] private bool debugLogging = true;

if (debugLogging)
{
    Debug.Log($"[MWAWalletAdapter] Connection status: {_isConnected}");
}
```

**2. Check Unity Logs:**
- Window → Analysis → Profiler
- Check for errors in Console
- Use Logcat for Android logs

**3. Verify SDK Version:**
- Window → Package Manager
- Check Solana Unity SDK version
- Update if needed

**4. Test Network Connectivity:**
- Verify backend API is accessible
- Check firewall settings
- Test with `curl` or Postman

---

## Important Constants

Solana configuration is managed through Unity Inspector or code:

**MWA Configuration** ([MWAWalletAdapter.cs](Assets/Scripts/Auth/MWAWalletAdapter.cs)):
```csharp
[SerializeField] private string appIdentityName = "Solracer";
[SerializeField] private string appIdentityUri = "https://solracer.com";
[SerializeField] private string appIconUri = "/favicon.ico";
[SerializeField] private RpcCluster rpcCluster = RpcCluster.DevNet;
```

**Why Inspector Serialization?**
- Easy to configure without code changes
- Different settings for dev/staging/prod
- No hardcoded values in code

---

## Resources

### Official Documentation
- [Solana Unity SDK](https://github.com/magicblock-labs/Solana.Unity-SDK)
- [Solana Mobile Wallet Adapter](https://docs.solanamobile.com/)
- [Solana Web3.js](https://solana-labs.github.io/solana-web3.js/)

### Developer Tools
- [Solana Explorer (Devnet)](https://explorer.solana.com/?cluster=devnet)
- [Solana Faucet](https://faucet.solana.com/)
- [Unity Profiler](https://docs.unity3d.com/Manual/Profiler.html)

### Sample Apps
- [Solana Unity SDK Examples](https://github.com/magicblock-labs/Solana.Unity-SDK/tree/main/Examples)

---

## Summary

This guide explained how Solracer integrates Solana Unity SDK for:
- **Wallet Connection**: MWA adapter for Seeker devices
- **Transaction Signing**: Backend builds, Unity signs via MWA
- **On-Chain Operations**: Race creation/joining with Solana program

Key takeaways:
- Solana Unity SDK provides built-in MWA support
- Backend handles transaction building, Unity only signs
- MWA triggers native wallet bottom sheet on Seeker
- Error handling is critical for good UX

For quick start instructions, see [README.md](README.md).






