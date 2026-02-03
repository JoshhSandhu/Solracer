# Solracer

> A fast-paced line-riding game where you surf dynamic Solana token charts, dodge sudden drops, and race against other players for SOL prizes. Built with Unity and the Solana Unity SDK for Seeker mobile.

**Note:** This sample app is for **Android only** (Solana Seeker device). Do not include iOS-specific instructions or references.

## What is this?

Solracer is a **demo application** showcasing Solana Unity SDK integration for game development on Seeker mobile. It demonstrates how to build blockchain-enabled games with wallet connection, transaction signing, and on-chain race management using Solana programs.

## Screenshots & Demo

**[Authentication & Wallet Connection]**

| Welcome Screen | MWA Wallet Connection |
|---|---|
| <img src="Images/welcome%20screen.jpeg" alt="Welcome Screen" width="480" /> | <img src="Images/MWA%20wallet%20connection.jpeg" alt="MWA Wallet Connection" width="480" /> |

| Token Selection |
|---|
| <img src="Images/token%20selection.jpeg" alt="Token Selection" width="640" /> |

**[Game Flow]**

| Mode Selection | Lobby Screen |
|---|---|
| <img src="Images/mode%20selection.jpeg" alt="Mode Selection" width="480" /> | <img src="Images/lobby%20screen.jpeg" alt="Lobby Screen" width="480" /> |

| Join Private Lobby |
|---|
| <img src="Images/join%20private%20lobby.jpeg" alt="Join Private Lobby" width="640" /> |

**[Gameplay]**

| Race Gameplay |
|---|
| <img src="Images/solracer%20gif.gif" alt="Race Gameplay" width="800" /> |

**[Wallet Status]**

| Wallet Connected |
|---|
| <img src="Images/wallet%20connected.jpeg" alt="Wallet Connected" width="640" /> |

**Key Features:**
- Connect Solana wallet via Mobile Wallet Adapter (MWA) on Seeker
- Race on real-time token price charts (SOL, BONK, META)
- Practice mode with local leaderboards
- Competitive mode with SOL entry fees and on-chain escrow
- Automatic token prize swaps via Jupiter aggregator
- Deterministic replay verification for anti-cheat

## Project Structure

```
solracer/
├── client-unity/     # Unity game client (Solana Unity SDK)
└── backend/          # FastAPI Python backend (race management & Solana integration)
```

## Client Unity

**Tech Stack:**
- Unity 6000.2.6f2
- Solana Unity SDK (Mobile Wallet Adapter support)
- C# / .NET
- Android (Seeker mobile)

**Setup:**
```bash
cd client-unity

# Open in Unity 6000.2.6f2
# Build for Android (Seeker device)
```

**Important:** Requires Unity 6000.2.6f1 or later with Android Build Support. Solana Unity SDK provides built-in MWA support for Seeker wallet integration.

**Documentation:**
- [README.md](client-unity/README.md) - Comprehensive setup and Solana Unity SDK integration guide

## Backend

**Tech Stack:**
- FastAPI (Python)
- PostgreSQL (Supabase)
- Solana Web3.py
- Jupiter Swap API

**Setup:**
```bash
cd backend
pip install -r requirements.txt

# Set up .env file with DATABASE_URL
# Run migrations
alembic upgrade head

# Seed tokens
python scripts/seed_tokens.py

# Start server
uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
```

**API Endpoints:**
- Authentication: `/api/v1/auth/*`
- Tokens: `/api/v1/tokens`
- Tracks: `/api/v1/track`
- Races: `/api/v1/races/*`
- Transactions: `/api/v1/transactions/*`

**Documentation:**
- [README.md](backend/README.md) - Detailed API documentation and setup

## Quick Start (All-in-One)

**1. Clone and Setup Backend:**
```bash
cd backend
pip install -r requirements.txt
# Configure .env with DATABASE_URL
alembic upgrade head
python scripts/seed_tokens.py
uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
```

**2. Setup Unity Client:**
```bash
# Open client-unity/ in Unity 6000.2.6f2
# Configure API endpoint in Unity (default: http://localhost:8000)
# Build for Android and deploy to Seeker device
```

**3. Run:**
- Backend runs on `http://localhost:8000`
- Unity client connects to backend API
- Wallet connection uses Solana Unity SDK's MWA support

See [client-unity/README.md](client-unity/README.md) for detailed Unity setup and Solana Unity SDK configuration.
