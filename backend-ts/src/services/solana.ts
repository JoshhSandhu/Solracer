/**
 * Solana RPC connection — mirrors backend/app/services/solana_client.py.
 */

import { Connection, type Commitment } from "@solana/web3.js";

const RPC_URL = process.env.SOLANA_RPC_URL ?? "https://api.devnet.solana.com";
const COMMITMENT: Commitment =
  (process.env.SOLANA_COMMITMENT as Commitment) ?? "confirmed";

let _connection: Connection | null = null;

export function getConnection(): Connection {
  if (!_connection) {
    _connection = new Connection(RPC_URL, COMMITMENT);
    console.log(`[Solana] Connected to ${RPC_URL} (commitment: ${COMMITMENT})`);
  }
  return _connection;
}
