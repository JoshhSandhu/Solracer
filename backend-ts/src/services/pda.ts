import { PublicKey } from "@solana/web3.js";

const DEFAULT_PROGRAM_ID = "5Qe7B4LEMjmfbWgt2ctKY8ZzesDobubBi79HwPABJFkQ";

let _programId: PublicKey | null = null;

export function getProgramId(): PublicKey {
  if (!_programId) {
    const id = process.env.SOLANA_PROGRAM_ID ?? DEFAULT_PROGRAM_ID;
    _programId = new PublicKey(id);
  }
  return _programId;
}

export function deriveRacePda(
  raceId: string,
  tokenMint: PublicKey,
  entryFeeLamports: bigint,
): [PublicKey, number] {
  const entryFeeBytes = Buffer.alloc(8);
  entryFeeBytes.writeBigUInt64LE(entryFeeLamports);

  const seeds = [
    Buffer.from("race"),
    Buffer.from(raceId, "utf-8"),
    tokenMint.toBuffer(),
    entryFeeBytes,
  ];

  return PublicKey.findProgramAddressSync(seeds, getProgramId());
}
