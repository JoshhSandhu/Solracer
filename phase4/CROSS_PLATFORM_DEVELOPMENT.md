# Cross-Platform Development: Program on Arch PC, Backend on Windows

## ✅ Yes, This Works Perfectly!

You can develop the Solana program on your Arch Linux PC and use the backend on Windows. Here's what you need:

---

## What You Need to Transfer from Arch PC to Backend

### 1. **IDL File** (Required)
- **File**: `solracer-program/target/idl/solracer.json`
- **Location**: Copy to `backend/app/idl/solracer.json`
- **Purpose**: Backend uses this to build instructions and understand program interface

### 2. **Program ID** (Required)
- **What**: The public key of your deployed program
- **How to get**: `anchor keys list` or from deployment output
- **Where**: Add to `backend/.env` as `SOLANA_PROGRAM_ID=YOUR_PROGRAM_ID_HERE`
- **Purpose**: Backend needs this to call your program

### 3. **Deployed Program** (Required)
- **What**: The program must be deployed to devnet/mainnet
- **Where**: Lives on Solana blockchain (accessible via RPC)
- **Purpose**: Backend calls this deployed program

### 4. **Program Keypair** (Optional - only if backend needs to sign)
- **What**: The program's keypair file (usually `target/deploy/solracer-keypair.json`)
- **When needed**: Only if backend needs to sign transactions as program authority
- **For most cases**: NOT needed (users sign their own transactions)

---

## Development Workflow

### On Arch Linux PC (Program Development)

```bash
# 1. Develop program
cd solracer-program
anchor build
anchor test

# 2. Deploy to devnet
anchor deploy --provider.cluster devnet

# 3. Get program ID
anchor keys list
# Output: solracer: YOUR_PROGRAM_ID_HERE

# 4. Copy IDL to shared location
cp target/idl/solracer.json /path/to/shared/solracer.json
# OR upload to cloud storage
# OR commit to git (if not sensitive)
```

### On Windows PC (Backend Development)

```bash
# 1. Copy IDL file to backend
# From Arch PC: solracer-program/target/idl/solracer.json
# To Windows: backend/app/idl/solracer.json

# 2. Update backend/.env
SOLANA_PROGRAM_ID=YOUR_PROGRAM_ID_FROM_ARCH_PC
PROGRAM_IDL_PATH=app/idl/solracer.json
SOLANA_RPC_URL=https://api.devnet.solana.com
SOLANA_NETWORK=devnet

# 3. Backend can now interact with program
# No Rust/Anchor needed on Windows!
```

---

## What Backend Needs (No Rust/Anchor Required!)

### ✅ Backend Has:
- Python Solana libraries (`solana-py`, `anchorpy`, `solders`)
- IDL file (JSON)
- Program ID
- RPC endpoint

### ❌ Backend Does NOT Need:
- Rust compiler
- Anchor CLI
- Solana CLI
- Program source code
- Ability to build programs

---

## How Backend Uses IDL

The IDL (Interface Definition Language) file tells the backend:

1. **What instructions exist** (e.g., `create_race`, `join_race`)
2. **What parameters each instruction needs**
3. **What accounts each instruction requires**
4. **How to serialize data**

Example backend code:

```python
# backend/app/services/program_client.py
from anchorpy import Idl, Program
from solana.rpc.api import Client
import json

# Load IDL
with open("app/idl/solracer.json") as f:
    idl_json = json.load(f)
idl = Idl.from_json(idl_json)

# Create program instance
program = Program(idl, program_id, rpc_client)

# Build instruction using IDL
instruction = program.instruction.create_race(
    entry_fee=1000000,  # 0.001 SOL
    ctx=Context(
        accounts={
            "race": race_pda,
            "player1": player1_pubkey,
            "system_program": SYSTEM_PROGRAM_ID,
        },
        signers=[player1_keypair],
    )
)
```

---

## Transfer Methods

### Option 1: Git Repository (Recommended)
```bash
# On Arch PC
cd solracer-program
git add target/idl/solracer.json
git commit -m "Add IDL for backend"
git push

# On Windows PC
git pull
cp solracer-program/target/idl/solracer.json backend/app/idl/solracer.json
```

### Option 2: Shared Network Drive
```bash
# Copy via network share or USB
# From Arch: cp target/idl/solracer.json /mnt/shared/
# On Windows: Copy from shared drive to backend/app/idl/
```

### Option 3: Cloud Storage
```bash
# Upload IDL to Google Drive/Dropbox/etc
# Download on Windows
```

### Option 4: Direct Copy (if both PCs accessible)
```bash
# Use scp, rsync, or file sharing
scp target/idl/solracer.json windows-pc:/path/to/backend/app/idl/
```

---

## Important Notes

### 1. **IDL Must Match Deployed Program**
- If you update the program on Arch PC, you MUST:
  1. Redeploy the program
  2. Regenerate IDL (`anchor build`)
  3. Copy new IDL to backend
  4. Restart backend

### 2. **Program Must Be Deployed**
- Backend can't use a program that only exists locally
- Must deploy to devnet (for testing) or mainnet (for production)
- Deployment happens on Arch PC, but program lives on blockchain

### 3. **Network Must Match**
- If program deployed to devnet → backend must use devnet RPC
- If program deployed to mainnet → backend must use mainnet RPC
- Set `SOLANA_NETWORK` in backend `.env` to match

### 4. **Version Compatibility**
- IDL version should match deployed program version
- If you change program logic, redeploy and update IDL

---

## Quick Checklist

### On Arch PC (One-Time Setup):
- [ ] Install Rust, Solana CLI, Anchor
- [ ] Create Anchor project
- [ ] Develop program
- [ ] Deploy to devnet
- [ ] Get program ID
- [ ] Copy IDL file to accessible location

### On Windows PC (Backend Setup):
- [ ] Install Python Solana libraries (`pip install solana anchorpy solders`)
- [ ] Copy IDL file to `backend/app/idl/solracer.json`
- [ ] Add `SOLANA_PROGRAM_ID` to `backend/.env`
- [ ] Add `PROGRAM_IDL_PATH` to `backend/.env`
- [ ] Set `SOLANA_RPC_URL` and `SOLANA_NETWORK` in `backend/.env`
- [ ] Backend can now interact with program!

---

## Summary

**Yes, you can develop Phase 4 on a different Arch PC!**

**What you need to transfer:**
1. ✅ IDL file (`solracer.json`)
2. ✅ Program ID (public key)
3. ✅ Program must be deployed to blockchain

**What backend needs:**
- ✅ IDL file
- ✅ Program ID
- ✅ Python Solana libraries
- ❌ NO Rust/Anchor/Solana CLI needed!

This is exactly the **Hybrid Approach (Option C)** - perfect for your setup!

