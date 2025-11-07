# Phase 4: Solana Program Integration - Setup Guide

## Overview

Phase 4 involves integrating Solana on-chain programs for race management. This guide explains the setup requirements, different approaches, and how transactions will be sent to the Solana blockchain.

**Important**: This guide assumes **cross-platform development**:
- **Solana Program Development**: On Arch Linux PC (or separate machine)
- **Backend Development**: On Windows PC (current setup)
- **What Backend Needs**: Only IDL file and Program ID (no Rust/Anchor needed!)

---

## Key Questions Answered

### 1. Do we need Anchor and Solana CLI?

**Short Answer**: Only on the Arch PC where you develop the Solana program. The Windows backend does NOT need Anchor or Solana CLI.

**Options**:
- **Option A: Full Solana Program Development** (requires Anchor + Solana CLI on Arch PC only)
- **Option B: Backend-Only Integration** (no Anchor/Solana CLI needed anywhere)
- **Option C: Hybrid Approach** (programs deployed separately, backend interacts) - **RECOMMENDED**

---

## Option A: Full Solana Program Development

**📖 See [OPTION_A_DETAILED_GUIDE.md](./OPTION_A_DETAILED_GUIDE.md) for complete technical details, code examples, and implementation guide.**

### Quick Overview

**Local Development**:
- ✅ **Rust** (latest stable version)
- ✅ **Solana CLI** (`solana-install init`)
- ✅ **Anchor** (`avm install latest`)
- ✅ **Anchor CLI** (`anchor --version`)
- ✅ **Solana keypair** (for deploying programs)

**What we'll build**:
- Custom Solana program (Rust) for race management
- Program handles: race creation, entry fees, race settlement, payouts
- Backend calls program instructions
- Complete on-chain race state management

**Pros**:
- ✅ Full control over on-chain logic
- ✅ Custom race management program
- ✅ On-chain race state (trustless)
- ✅ Secure escrow (PDA-based)
- ✅ Decentralized winner determination

**Cons**:
- ⚠️ More complex setup
- ⚠️ Requires Rust knowledge (you have this!)
- ⚠️ Longer development time
- ⚠️ Transaction fees for operations
- ⚠️ Account size/compute limits

**Best For**: Production-ready, trustless race management with full decentralization.

---

## Option B: Backend-Only Integration (Recommended for MVP)

### Requirements

**Backend Only**:
- ✅ **Python Solana libraries**: `solana-py`, `solders`, `anchorpy`
- ✅ **Solana RPC endpoint** (public or custom)
- ✅ **Wallet keypair** (for backend operations)

**No Local Requirements**:
- ❌ No Anchor needed locally
- ❌ No Solana CLI needed locally
- ❌ No Rust compilation needed

**What we'll do**:
- Backend builds and sends transactions
- Uses existing Solana programs (SPL Token, System Program)
- Unity client signs transactions (raw bytes)
- Backend submits signed transactions

**Pros**:
- Faster to implement
- No Rust/Anchor knowledge needed
- Simpler setup
- Good for MVP

**Cons**:
- Less on-chain logic
- Race state primarily off-chain (backend)
- May need custom program later

---

## Option C: Hybrid Approach

### Requirements

**Program Deployment** (one-time, separate):
- Deploy Solana program using Anchor (on separate machine/CI)
- Program address stored in backend config

**Backend Integration**:
- Backend interacts with deployed program
- Uses Python Solana libraries
- No local Anchor/Solana CLI needed for daily development

**Pros**:
- Best of both worlds
- Custom on-chain logic
- Backend doesn't need full Solana toolchain

**Cons**:
- Initial program deployment complexity
- Two separate codebases to maintain

---

## Recommended Approach: Option C (Hybrid - Custom Program + Backend)

### Why Option C? (Since you know Rust & Solana!)

1. **Leverage Your Skills**: Use your Rust/Solana expertise
2. **On-Chain Race State**: Trustless race management
3. **Better Architecture**: Program handles race logic, backend handles API/UI
4. **Production Ready**: More decentralized and secure
5. **Scalable**: On-chain state scales better than off-chain
6. **Cross-Platform Friendly**: Develop program on Arch PC, backend on Windows

### Cross-Platform Development Setup

**Arch Linux PC (Program Development)**:
- ✅ Install Rust, Solana CLI, Anchor
- ✅ Develop Solana program
- ✅ Deploy to devnet/mainnet
- ✅ Generate IDL file

**Windows PC (Backend Development)**:
- ✅ Only needs IDL file (JSON)
- ✅ Only needs Program ID (public key)
- ✅ Python Solana libraries (`solana-py`, `anchorpy`)
- ❌ NO Rust/Anchor/Solana CLI needed!

**Transfer from Arch PC to Windows Backend**:
1. Copy `solracer-program/target/idl/solracer.json` → `backend/app/idl/solracer.json`
2. Get Program ID from deployment → Add to `backend/.env` as `SOLANA_PROGRAM_ID`
3. That's it! Backend can now interact with your deployed program.

📖 **See [CROSS_PLATFORM_DEVELOPMENT.md](./CROSS_PLATFORM_DEVELOPMENT.md) for detailed workflow.**

### What We'll Build

**Solana Program (Rust/Anchor)**:
- Race creation instruction (creates PDA, stores race state)
- Join race instruction (adds player2, locks entry fees)
- Submit result instruction (stores result on-chain)
- Settle race instruction (determines winner, releases funds)
- Payout instruction (transfers prize to winner)

**Backend Responsibilities**:
- Build Solana transactions (calling program instructions)
- Sync off-chain database with on-chain state
- Handle API endpoints for Unity
- Submit transactions to Solana
- Monitor race state on-chain

**Unity Client Responsibilities**:
- Sign transactions (raw bytes)
- Display transaction status
- Handle wallet interactions (via Privy)

**On-Chain State**:
- Race PDA accounts (race state, entry fees escrow)
- Race results (on-chain verification)
- Winner determination (trustless)
- Prize escrow (locked until settlement)

---

## Transaction Flow

### Current Architecture (Phase 2-3)

```
Unity Client → Privy Wallet → Sign Message/Transaction
```

### Phase 4 Architecture (With Custom Program)

```
Unity Client → Privy Wallet → Sign Transaction (raw bytes)
                ↓
Backend → Build Transaction (with program instruction) → Send to Unity
                ↓
Unity → Sign → Return Signed Transaction
                ↓
Backend → Submit to Solana → Program Executes → Return TX Signature
                ↓
Backend → Sync On-Chain State → Update Database
```

### Detailed Flow

**1. Race Creation**:
```
Unity: User clicks "Create Race"
  ↓
Backend: Builds Solana transaction
  - Calls program: create_race instruction
  - Derives race PDA (race_id, token_mint, entry_fee)
  - Transfers SOL (entry fee) to PDA escrow
  - Creates race account on-chain
  ↓
Backend → Unity: Returns transaction (raw bytes, base64)
  ↓
Unity → Privy: Sign transaction
  ↓
Unity → Backend: Returns signed transaction
  ↓
Backend: Submit to Solana network
  ↓
Backend: Program creates race PDA account
  ↓
Backend: Sync on-chain state to database
  ↓
Backend: Store race_id, PDA address, and TX signature
```

**2. Join Race**:
```
Unity: Player 2 clicks "Join Race"
  ↓
Backend: Builds Solana transaction
  - Calls program: join_race instruction
  - Transfers SOL (entry fee) to PDA escrow
  - Updates race account (adds player2)
  ↓
Unity → Privy: Sign transaction
  ↓
Backend: Submit to Solana
  ↓
Backend: Program updates race state on-chain
  ↓
Backend: Sync state, update database (status → ACTIVE)
```

**3. Submit Result**:
```
Unity: Player finishes race
  ↓
Backend: Builds Solana transaction
  - Calls program: submit_result instruction
  - Stores result in race account
  ↓
Unity → Privy: Sign transaction
  ↓
Backend: Submit to Solana
  ↓
Backend: Program stores result on-chain
  ↓
Backend: Sync state, update database
```

**4. Race Settlement**:
```
Backend: Both results submitted on-chain
  ↓
Backend: Builds Solana transaction
  - Calls program: settle_race instruction
  - Program determines winner (on-chain)
  - Program unlocks escrow
  ↓
Backend: Sign with backend wallet (or winner signs)
  ↓
Backend: Submit to Solana
  ↓
Backend: Program settles race, unlocks funds
  ↓
Backend: Sync state, update database (status → SETTLED)
```

**5. Claim Prize**:
```
Unity: Winner clicks "Claim Prize"
  ↓
Backend: Builds Solana transaction
  - Calls program: claim_prize instruction
  - Transfers prize from escrow to winner
  - Or swaps SOL → Token → Transfer
  ↓
Unity → Privy: Sign transaction
  ↓
Backend: Submit to Solana
  ↓
Backend: Program transfers prize
  ↓
Backend: Update payout status
```

---

## Environment Setup (Option C - Custom Program)

### Solana Program Setup (Rust/Anchor) - On Arch Linux PC

**Location**: Develop on your Arch Linux PC (or separate machine)

**Requirements** (Fresh Install):
```bash
# On Arch Linux PC (as your user, e.g., LynxMain)

# 1. Install Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
source ~/.cargo/env

# 2. Install Solana CLI
sh -c "$(curl -sSfL https://release.solana.com/stable/install)"
export PATH="$HOME/.local/share/solana/install/active_release/bin:$PATH"
echo 'export PATH="$HOME/.local/share/solana/install/active_release/bin:$PATH"' >> ~/.bashrc

# 3. Install Anchor
cargo install --git https://github.com/coral-xyz/anchor avm --locked --force
avm install latest
avm use latest

# 4. Verify installations
rustc --version
cargo --version
solana --version
anchor --version
```

**Project Structure** (on Arch PC):
```
solracer-program/
├── Anchor.toml
├── Cargo.toml
├── programs/
│   └── solracer/
│       ├── Cargo.toml
│       └── src/
│           └── lib.rs          # Main program logic
├── tests/
│   └── solracer.ts             # Anchor tests
└── target/
    └── idl/
        └── solracer.json        # Generated IDL (COPY THIS TO BACKEND!)
```

**Program Instructions**:
- `create_race` - Create new race PDA, escrow entry fees
- `join_race` - Player2 joins, locks their entry fee
- `submit_result` - Store race result on-chain
- `settle_race` - Determine winner, unlock escrow
- `claim_prize` - Winner claims prize tokens

**After Deployment**:
1. Deploy to devnet: `anchor deploy --provider.cluster devnet`
2. Get Program ID: `anchor keys list`
3. **Copy IDL to Windows**: `cp target/idl/solracer.json /path/to/shared/` or commit to git
4. **Transfer to Windows backend**: Copy to `backend/app/idl/solracer.json`

### Backend Requirements - On Windows PC

**Python Dependencies**:
```python
# requirements.txt additions
solana==0.30.2
solders==0.18.1
anchorpy==0.18.0
base58==2.1.1
```

**Environment Variables**:
```bash
# backend/.env
SOLANA_RPC_URL=https://api.devnet.solana.com  # or mainnet-beta
SOLANA_NETWORK=devnet  # or mainnet-beta
SOLANA_PROGRAM_ID=YOUR_PROGRAM_ID_FROM_ARCH_PC  # From anchor keys list
BACKEND_WALLET_PRIVATE_KEY=...  # Base58 encoded keypair (for admin operations)
PROGRAM_IDL_PATH=app/idl/solracer.json  # Path to IDL file (copied from Arch PC)
```

**Setup Steps**:
1. Copy IDL file from Arch PC: `solracer-program/target/idl/solracer.json` → `backend/app/idl/solracer.json`
2. Get Program ID from Arch PC deployment and add to `.env`
3. Install Python dependencies: `pip install solana solders anchorpy base58`
4. Backend can now interact with your deployed program!

**Backend Wallet**:
- Used for: Admin operations, monitoring, syncing state
- **NOT** used for: User entry fees (users sign their own transactions)
- **NOT** used for: Race creation (users sign their own transactions)

### Unity Requirements

**No Changes Needed**:
- Privy already handles wallet signing
- We'll extend `AuthenticationFlowManager.cs` to sign transactions (not just messages)

---

## How Transactions Are Sent

### Method 1: Backend Builds, Unity Signs, Backend Submits

**Flow**:
1. Backend builds transaction (using `solana-py`)
2. Backend serializes transaction to base64 bytes
3. Backend sends bytes to Unity via API
4. Unity calls Privy to sign bytes
5. Unity returns signed transaction to backend
6. Backend submits to Solana RPC

**Pros**:
- Backend controls transaction structure
- Unity only needs to sign
- Easier to update transaction logic

**Cons**:
- Requires API round-trip
- More complex flow

### Method 2: Unity Builds, Signs, Backend Submits

**Flow**:
1. Unity builds transaction (using Solana Web3.js or similar)
2. Unity signs with Privy
3. Unity sends signed transaction to backend
4. Backend submits to Solana RPC

**Pros**:
- Simpler backend
- Unity has full control

**Cons**:
- Requires Solana libraries in Unity
- More complex Unity code
- Harder to update transaction logic

### Recommended: Method 1

**Why**: Backend has more control, easier to update, Unity stays simple.

---

## Implementation Plan

### Phase 4.1: Solana Program Development (Rust/Anchor) - On Arch Linux PC

**Location**: Develop on Arch Linux PC

**Tasks**:
1. Set up Anchor project (on Arch PC)
2. Define program instructions (create_race, join_race, submit_result, settle_race)
3. Implement PDA account structure
4. Implement escrow logic
5. Write tests
6. Deploy to devnet
7. Generate IDL for backend
8. **Copy IDL to Windows backend**: `target/idl/solracer.json` → `backend/app/idl/solracer.json`
9. **Get Program ID**: `anchor keys list` → Add to `backend/.env`

**Files** (on Arch PC):
- `solracer-program/programs/solracer/src/lib.rs` - Program logic
- `solracer-program/tests/solracer.ts` - Anchor tests
- `solracer-program/target/idl/solracer.json` - Generated IDL (COPY TO BACKEND!)

**Transfer to Windows**:
- Copy `target/idl/solracer.json` to `backend/app/idl/solracer.json`
- Add Program ID to `backend/.env` as `SOLANA_PROGRAM_ID`

### Phase 4.2: Backend Solana Integration - On Windows PC

**Location**: Develop on Windows PC (current setup)

**Prerequisites**:
- ✅ IDL file copied from Arch PC: `backend/app/idl/solracer.json`
- ✅ Program ID added to `backend/.env`: `SOLANA_PROGRAM_ID=...`

**Tasks**:
1. Install Python Solana libraries (`pip install solana solders anchorpy base58`)
2. Set up Solana RPC connection
3. Load program IDL (from file copied from Arch PC)
4. Create backend wallet keypair
5. Implement instruction building (using IDL)
6. Implement transaction building functions
7. Implement transaction submission functions
8. Implement on-chain state syncing

**Files** (on Windows):
- `backend/app/idl/solracer.json` - IDL file (copied from Arch PC)
- `backend/app/services/solana_client.py` - Solana RPC client
- `backend/app/services/program_client.py` - Program instruction builder (using IDL)
- `backend/app/services/transaction_builder.py` - Build transactions
- `backend/app/services/transaction_submitter.py` - Submit transactions
- `backend/app/services/onchain_sync.py` - Sync on-chain state to database

**Note**: No Rust/Anchor/Solana CLI needed on Windows!

### Phase 4.3: Race On-Chain Integration

**Tasks**:
1. Build `create_race` instruction transaction
2. Build `join_race` instruction transaction
3. Build `submit_result` instruction transaction
4. Build `settle_race` instruction transaction
5. Store transaction signatures in database
6. Sync on-chain race state to database
7. Integrate with existing race endpoints

**Files**:
- `backend/app/api/routes/races.py` - Add Solana transaction endpoints
- `backend/app/services/race_onchain.py` - On-chain race logic
- `backend/app/services/pda_utils.py` - PDA derivation helpers

### Phase 4.4: Unity Transaction Signing

**Tasks**:
1. Extend `AuthenticationFlowManager.cs` to sign transactions (already exists!)
2. Add API endpoint to get transaction bytes (`POST /api/v1/races/build_transaction`)
3. Add API endpoint to submit signed transaction (`POST /api/v1/races/submit_transaction`)
4. Update race creation flow to include Solana transaction
5. Add transaction status polling

**Files**:
- `client-unity/Assets/Scripts/Auth/AuthenticationFlowManager.cs` - Already has `SignTransaction()` method
- `client-unity/Assets/Scripts/API/RaceAPIClient.cs` - Add transaction endpoints
- `client-unity/Assets/Scripts/UI/RaceCreationUI.cs` - Update UI flow

### Phase 4.5: Payout System

**Tasks**:
1. Implement SOL → Token swap (Jupiter API)
2. Build `claim_prize` instruction transaction
3. Handle winner payout (on-chain)
4. Update race settlement flow
5. Monitor payout transactions

**Files**:
- `backend/app/services/jupiter_swap.py` - Token swap integration
- `backend/app/services/payout_handler.py` - Payout logic
- `backend/app/services/prize_claim.py` - Prize claiming logic

---

## Setup Instructions (Option B)

### Step 1: Install Python Dependencies

```bash
cd backend
pip install solana==0.30.2 solders==0.18.1 anchorpy==0.18.0 base58==2.1.1
```

### Step 2: Set Up Backend Wallet

**Generate Keypair** (Python):
```python
from solders.keypair import Keypair
import base58

# Generate new keypair
keypair = Keypair()
private_key = base58.b58encode(bytes(keypair)).decode('utf-8')
public_key = str(keypair.pubkey())

print(f"Private Key: {private_key}")
print(f"Public Key: {public_key}")
```

**Store in `.env`**:
```bash
BACKEND_WALLET_PRIVATE_KEY=your_base58_private_key_here
```

**Fund Wallet** (Devnet):
```bash
# Get SOL from faucet
solana airdrop 2 <PUBLIC_KEY> --url devnet
```

### Step 3: Configure RPC Endpoint

**Free Options**:
- Public RPC: `https://api.devnet.solana.com` (rate limited)
- Helius: Free tier available
- QuickNode: Free tier available

**Production**:
- Use dedicated RPC provider (Helius, QuickNode, Alchemy)

**`.env`**:
```bash
SOLANA_RPC_URL=https://api.devnet.solana.com
SOLANA_NETWORK=devnet
```

### Step 4: Test Connection

```python
from solana.rpc.api import Client

client = Client("https://api.devnet.solana.com")
version = client.get_version()
print(version)
```

---

## Alternative: Using Existing Solana Programs

### What We Can Do Without Custom Program

**1. SOL Transfers**:
- Use System Program
- Transfer entry fees to escrow
- Transfer prizes to winners

**2. Token Transfers**:
- Use SPL Token Program
- Transfer prize tokens to winners

**3. Race State**:
- Store off-chain (backend database)
- Use transaction signatures as proof

**4. Escrow**:
- Use Program Derived Address (PDA) as escrow
- Or use simple wallet as escrow

**This is sufficient for MVP!**

---

## When to Add Custom Solana Program

**Add custom program if**:
- Need on-chain race state verification
- Need complex escrow logic
- Need on-chain matchmaking
- Need decentralized race management
- Scaling to thousands of races

**For MVP**: Not necessary!

---

## Summary

### Recommended Setup (Option C - Custom Program)

**Solana Program (Rust/Anchor)**:
- ✅ Anchor framework
- ✅ Solana CLI (for deployment)
- ✅ Custom race management program
- ✅ PDA-based race accounts
- ✅ On-chain race state

**Backend**:
- ✅ Python Solana libraries (`solana-py`, `solders`, `anchorpy`)
- ✅ Backend wallet keypair (for admin operations)
- ✅ Solana RPC endpoint
- ✅ Program IDL loading (for instruction building)
- ✅ Transaction building (calling program instructions)

**Unity**:
- ✅ Privy wallet (already set up)
- ✅ Transaction signing (extend existing code)
- ❌ No Solana libraries needed

**Transaction Flow**:
1. Backend builds transaction (with program instruction) → sends bytes to Unity
2. Unity signs with Privy → returns to backend
3. Backend submits to Solana → returns TX signature
4. Backend syncs on-chain state to database

**This approach is**:
- ✅ Leverages your Rust/Solana expertise
- ✅ More decentralized and trustless
- ✅ On-chain race state verification
- ✅ Production-ready architecture
- ✅ Better scalability

---

## Solana Program Architecture (High-Level)

### Program Structure

```rust
// Anchor program structure
#[program]
pub mod solracer {
    use super::*;

    pub fn create_race(ctx: Context<CreateRace>, ...) -> Result<()> {
        // Create race PDA
        // Escrow entry fee
        // Initialize race state
    }

    pub fn join_race(ctx: Context<JoinRace>, ...) -> Result<()> {
        // Add player2
        // Escrow player2 entry fee
        // Update race state
    }

    pub fn submit_result(ctx: Context<SubmitResult>, ...) -> Result<()> {
        // Store result on-chain
        // Verify player is in race
    }

    pub fn settle_race(ctx: Context<SettleRace>) -> Result<()> {
        // Determine winner
        // Unlock escrow
        // Update race state
    }

    pub fn claim_prize(ctx: Context<ClaimPrize>) -> Result<()> {
        // Transfer prize to winner
    }
}
```

### Account Structure

```rust
#[account]
pub struct Race {
    pub race_id: String,           // Deterministic race ID
    pub token_mint: Pubkey,        // Token for race
    pub entry_fee_sol: u64,        // Entry fee in lamports
    pub player1: Pubkey,           // Player 1 wallet
    pub player2: Option<Pubkey>,   // Player 2 wallet (optional)
    pub status: RaceStatus,        // WAITING, ACTIVE, SETTLED
    pub player1_result: Option<RaceResult>,  // Player 1 result
    pub player2_result: Option<RaceResult>,  // Player 2 result
    pub winner: Option<Pubkey>,     // Winner wallet
    pub escrow_amount: u64,        // Total escrowed SOL
    pub created_at: i64,            // Timestamp
}

#[derive(AnchorSerialize, AnchorDeserialize, Clone)]
pub struct RaceResult {
    pub finish_time_ms: u64,
    pub coins_collected: u64,
    pub input_hash: [u8; 32],      // SHA256 hash
}
```

### PDA Derivation

```rust
// Race PDA seeds
let (race_pda, bump) = Pubkey::find_program_address(
    &[
        b"race",
        race_id.as_bytes(),
        token_mint.as_ref(),
        entry_fee_sol.to_le_bytes().as_ref(),
    ],
    program_id,
);
```

---

## Next Steps

### On Arch Linux PC:
1. **Set up Solana development environment** (Rust, Solana CLI, Anchor)
2. **Create Anchor project** (Phase 4.1) - Anchor project, program instructions
3. **Deploy program to devnet** - Get program ID
4. **Copy IDL to Windows** - `target/idl/solracer.json` → `backend/app/idl/solracer.json`

### On Windows PC:
1. **Copy IDL file** from Arch PC to `backend/app/idl/solracer.json`
2. **Add Program ID** to `backend/.env` as `SOLANA_PROGRAM_ID`
3. **Set up backend Solana integration** (Phase 4.2) - Load IDL, build instructions
4. **Implement race on-chain integration** (Phase 4.3) - Connect backend to program
5. **Add Unity transaction signing** (Phase 4.4) - Extend existing signing
6. **Implement payout system** (Phase 4.5) - Prize claiming

---

## Cross-Platform Development Summary

**Arch Linux PC**:
- ✅ Rust, Solana CLI, Anchor installed
- ✅ Develop and deploy Solana program
- ✅ Generate IDL file
- ✅ Transfer IDL and Program ID to Windows

**Windows PC**:
- ✅ Only needs IDL file (JSON)
- ✅ Only needs Program ID (public key)
- ✅ Python Solana libraries
- ❌ NO Rust/Anchor/Solana CLI needed!

📖 **See [CROSS_PLATFORM_DEVELOPMENT.md](./CROSS_PLATFORM_DEVELOPMENT.md) for detailed workflow.**

---

**Ready to start Phase 4?** Begin with Phase 4.1 on your Arch Linux PC! 🚀

