# Phase 4.1: Solana Program Development - Quick Start Guide

## Overview

This guide is for setting up Solana program development on your **Arch Linux PC** (or separate machine). The backend on Windows will only need the IDL file and Program ID - no Rust/Anchor needed there!

---

## Step 1: Install Development Tools (Fresh Install)

**Location**: On your Arch Linux PC (as your user, e.g., LynxMain)

### Install Rust

```bash
# Install Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
source ~/.cargo/env

# Verify
rustc --version
cargo --version
```

### Install Solana CLI

```bash
# Install Solana CLI
sh -c "$(curl -sSfL https://release.solana.com/stable/install)"

# Add to PATH
export PATH="$HOME/.local/share/solana/install/active_release/bin:$PATH"
echo 'export PATH="$HOME/.local/share/solana/install/active_release/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc

# Verify
solana --version
```

### Install Anchor

```bash
# Install Anchor Version Manager (AVM)
cargo install --git https://github.com/coral-xyz/anchor avm --locked --force

# Install latest Anchor
avm install latest
avm use latest

# Verify
anchor --version
```

### Install Platform Tools SDK (Required for Anchor)

```bash
# Set Solana SDK path
export SDK_ROOT="$HOME/.local/share/solana/install/active_release"
export PATH="$SDK_ROOT/platform-tools/sbf:$PATH"
echo 'export SDK_ROOT="$HOME/.local/share/solana/install/active_release"' >> ~/.bashrc
echo 'export PATH="$SDK_ROOT/platform-tools/sbf:$PATH"' >> ~/.bashrc
source ~/.bashrc

# Download platform tools if needed
cd /tmp
LATEST_TAG=$(curl -s https://api.github.com/repos/anza-xyz/platform-tools/releases/latest | grep '"tag_name"' | cut -d '"' -f 4)
wget https://github.com/anza-xyz/platform-tools/releases/download/${LATEST_TAG}/solana-platform-tools-x86_64-unknown-linux-gnu.tar.bz2
tar jxf solana-platform-tools-x86_64-unknown-linux-gnu.tar.bz2
mkdir -p ~/.local/share/solana/install/active_release/platform-tools
cp -r solana-platform-tools/* ~/.local/share/solana/install/active_release/platform-tools/
```

## Step 2: Navigate to Project Directory

```bash
# Navigate to your project directory
cd /path/to/Solracer
# Or if using WSL mount:
cd /mnt/d/mislanious/Unity/Uinty_projects/Solracer
```

### Step 3: Create Anchor Project

```bash
# Create new Anchor project
anchor init solracer-program

# This will create:
# solracer-program/
# ├── Anchor.toml
# ├── Cargo.toml
# ├── programs/
# │   └── solracer/
# │       └── src/
# │           └── lib.rs
# └── tests/
#     └── solracer.ts
```

### Step 4: Build the Program

```bash
cd solracer-program
anchor build
```

**Note**: If you get SDK path errors, make sure platform tools are installed (see Step 1).

### Step 5: Configure Solana

```bash
# Set to devnet
solana config set --url devnet

# Generate keypair (if needed)
solana-keygen new

# Airdrop SOL for testing
solana airdrop 2
```

### Step 6: Deploy to Devnet

```bash
# Deploy program
anchor deploy --provider.cluster devnet

# Get program ID (IMPORTANT: Save this for Windows backend!)
anchor keys list
# Output: solracer: YOUR_PROGRAM_ID_HERE
```

### Step 7: Generate IDL and Transfer to Windows

```bash
# IDL is automatically generated in target/idl/
ls target/idl/solracer.json

# Copy IDL to Windows backend (choose one method):
# Method 1: Git commit
git add target/idl/solracer.json
git commit -m "Add IDL for backend"
git push

# Method 2: Copy to shared location
cp target/idl/solracer.json /path/to/shared/solracer.json

# Method 3: Direct copy (if Windows accessible)
# scp target/idl/solracer.json windows-pc:/path/to/backend/app/idl/
```

**On Windows PC**:
1. Copy `solracer.json` to `backend/app/idl/solracer.json`
2. Add Program ID to `backend/.env`: `SOLANA_PROGRAM_ID=YOUR_PROGRAM_ID_HERE`
3. Backend can now interact with your program!

## Project Structure

```
Solracer/
├── client-unity/          # Unity game client (Windows)
├── backend/               # FastAPI backend (Windows)
│   ├── app/
│   │   └── idl/
│   │       └── solracer.json  # IDL file (copied from Arch PC)
│   └── documentation/
└── solracer-program/      # Solana program (Arch Linux PC)
    ├── Anchor.toml
    ├── programs/
    │   └── solracer/
    │       └── src/
    │           └── lib.rs
    ├── tests/
    │   └── solracer.ts
    └── target/
        └── idl/
            └── solracer.json  # Generated IDL (COPY TO BACKEND!)
```

## What to Transfer to Windows Backend

After deploying your program, you need to transfer:

1. **IDL File**: `solracer-program/target/idl/solracer.json` → `backend/app/idl/solracer.json`
2. **Program ID**: From `anchor keys list` → Add to `backend/.env` as `SOLANA_PROGRAM_ID`

## Ready to Start Development!

### On Arch Linux PC:
1. ✅ Install Rust, Solana CLI, Anchor
2. ✅ Create Anchor project
3. ✅ Start writing Solana program instructions
4. ✅ Build and deploy to devnet
5. ✅ Generate IDL for backend integration
6. ✅ Copy IDL and Program ID to Windows

### On Windows PC:
1. ✅ Copy IDL file to `backend/app/idl/solracer.json`
2. ✅ Add Program ID to `backend/.env`
3. ✅ Install Python Solana libraries: `pip install solana solders anchorpy base58`
4. ✅ Backend can now interact with your deployed program!

**Let's begin Phase 4.1 on your Arch Linux PC!** 🚀

📖 **See [CROSS_PLATFORM_DEVELOPMENT.md](./CROSS_PLATFORM_DEVELOPMENT.md) for detailed transfer workflow.**

