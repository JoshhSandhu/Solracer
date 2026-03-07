import * as anchor from "@coral-xyz/anchor";
import { Program, Idl } from "@coral-xyz/anchor";
import { SolracerEr } from "../target/types/solracer_er";
import { Connection, Keypair, PublicKey, SystemProgram } from "@solana/web3.js";
import { createHash } from "crypto";
import {
    delegateBufferPdaFromDelegatedAccountAndOwnerProgram,
    delegationRecordPdaFromDelegatedAccount,
    delegationMetadataPdaFromDelegatedAccount,
    DELEGATION_PROGRAM_ID,
} from "@magicblock-labs/ephemeral-rollups-sdk";

function raceIdHash(raceId: string): number[] {
    return Array.from(createHash("sha256").update(raceId, "utf8").digest());
}

/** Poll until the tx is confirmed on-chain (up to 60s) */
async function waitForTx(connection: Connection, sig: string): Promise<void> {
    const deadline = Date.now() + 60_000;
    while (Date.now() < deadline) {
        const status = await connection.getSignatureStatus(sig);
        const conf = status?.value?.confirmationStatus;
        if (conf === "confirmed" || conf === "finalized") return;
        await new Promise(r => setTimeout(r, 2000));
    }
    throw new Error(`Timed out waiting for tx ${sig}`);
}

async function main() {
    console.log("=== Solracer MagicBlock ER Live Integration Test ===");

    const fs = require("fs");
    const secret = JSON.parse(fs.readFileSync(`${process.env.HOME}/.config/solana/id.json`));
    const walletKeypair = Keypair.fromSecretKey(new Uint8Array(secret));
    const wallet = new anchor.Wallet(walletKeypair);

    const baseConnection = new Connection("https://api.devnet.solana.com", "confirmed");
    const mbConnection = new Connection("https://devnet.magicblock.app", "confirmed");

    const baseProvider = new anchor.AnchorProvider(baseConnection, wallet, { commitment: "confirmed" });
    const mbProvider = new anchor.AnchorProvider(mbConnection, wallet, { commitment: "confirmed" });

    anchor.setProvider(baseProvider);
    const baseProgram = anchor.workspace.SolracerEr as Program<SolracerEr>;
    const mbProgram = new Program<SolracerEr>(baseProgram.idl as Idl, mbProvider) as unknown as Program<SolracerEr>;
    const programId = baseProgram.programId;

    const raceId = `live-test-race-${Date.now()}`;
    const sessionKey = Keypair.generate();
    const hash = raceIdHash(raceId);

    const [positionPda] = PublicKey.findProgramAddressSync(
        [Buffer.from("position"), Buffer.from(hash), wallet.publicKey.toBuffer()],
        programId
    );

    console.log(`Race ID:            ${raceId}`);
    console.log(`PlayerPosition PDA: ${positionPda.toBase58()}`);
    console.log(`Session key:        ${sessionKey.publicKey.toBase58()}`);

    // ── Step 1: Init on Base Devnet ──────────────────────────────────────────
    console.log("\n[1] Initializing PDA on Base Devnet...");
    const initTx = await baseProgram.methods
        .initPositionPda(hash, sessionKey.publicKey)
        .accounts({
            position: positionPda,
            player: wallet.publicKey,
            systemProgram: SystemProgram.programId,
        } as any)
        .rpc({ commitment: "confirmed" });
    console.log(`    Init Tx: ${initTx}`);
    await waitForTx(baseConnection, initTx);

    const acct = await baseConnection.getAccountInfo(positionPda, "confirmed");
    if (!acct) throw new Error("Position PDA not found after init — RPC lag?");
    console.log(`    Account: ${acct.data.length} bytes, owner ${acct.owner.toBase58()} ✔`);

    // ── Step 2: Delegate to MagicBlock ──────────────────────────────────────
    console.log("\n[2] Delegating PDA to MagicBlock ER...");
    const delegateSig = await baseProgram.methods
        .delegatePositionPda()
        .accounts({
            player: wallet.publicKey,
            position: positionPda,
            buffer: delegateBufferPdaFromDelegatedAccountAndOwnerProgram(positionPda, programId),
            delegationRecord: delegationRecordPdaFromDelegatedAccount(positionPda),
            delegationMetadata: delegationMetadataPdaFromDelegatedAccount(positionPda),
            delegationProgram: DELEGATION_PROGRAM_ID,
            program: programId,
            systemProgram: SystemProgram.programId,
        } as any)
        .rpc({ skipPreflight: true, commitment: "confirmed" });
    console.log(`    Delegate Tx: ${delegateSig}`);
    await waitForTx(baseConnection, delegateSig);

    const delegateTxInfo = await baseConnection.getTransaction(delegateSig, {
        maxSupportedTransactionVersion: 0,
        commitment: "confirmed",
    });
    if (delegateTxInfo?.meta?.err) {
        console.error("❌ Delegate tx failed on-chain:", JSON.stringify(delegateTxInfo.meta.err));
        console.error("   Logs:\n  ", delegateTxInfo.meta.logMessages?.join("\n   "));
        process.exit(1);
    }
    const logs = delegateTxInfo?.meta?.logMessages ?? [];
    console.log(`    On-chain logs:\n   `, logs.join("\n    "));
    console.log("    Delegation confirmed ✔");

    console.log("\n    Waiting 4s for ER sequencer sync...");
    await new Promise(r => setTimeout(r, 4000));

    // ── Step 3: Update on MagicBlock ER ─────────────────────────────────────
    console.log("\n[3] Sending position updates to MagicBlock ER devnet...");
    for (let i = 1; i <= 3; i++) {
        const updateTx = await mbProgram.methods
            .updatePosition(hash, 10.0 * i, 20.0 * i, 50.0, i, i)
            .accounts({
                position: positionPda,
                authority: sessionKey.publicKey,
            } as any)
            .signers([sessionKey])
            .rpc();
        console.log(`    Update ${i} Tx: ${updateTx}`);
    }

    const erAccount = await mbProgram.account.playerPosition.fetch(positionPda, "confirmed");
    console.log(`\nER State → X: ${erAccount.x}, Y: ${erAccount.y}, Seq: ${erAccount.seq}`);
    if (erAccount.seq !== 3) throw new Error(`Expected seq=3, got ${erAccount.seq}`);

    console.log("\n✅ INTEGRATION TEST SUCCESSFUL");
}

main().catch(err => {
    console.error("\n❌ TEST FAILED:", err?.message ?? err);
    process.exit(1);
});
