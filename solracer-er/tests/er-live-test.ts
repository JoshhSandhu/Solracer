import * as anchor from "@coral-xyz/anchor";
import { Program, Idl } from "@coral-xyz/anchor";
import { SolracerEr } from "../target/types/solracer_er";
import { Connection, Keypair, PublicKey, SystemProgram } from "@solana/web3.js";
import { createHash } from "crypto";

function raceIdHash(raceId: string): number[] {
    const hash = createHash("sha256").update(raceId, "utf8").digest();
    return Array.from(hash);
}

async function main() {
    console.log("=== Solracer MagicBlock ER Live Integration Test ===");

    const fs = require('fs');
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

    const raceId = `live-test-race-${Date.now()}`;
    const sessionKey = Keypair.generate();
    const hash = raceIdHash(raceId);

    const [positionPda] = PublicKey.findProgramAddressSync(
        [Buffer.from("position"), Buffer.from(hash), wallet.publicKey.toBuffer()],
        baseProgram.programId
    );

    console.log(`Race ID: ${raceId}`);
    console.log(`PlayerPosition PDA: ${positionPda.toBase58()}`);

    // Step 1: Init on Base Devnet
    console.log("\n[1] Initializing PDA on Base Devnet...");
    const initTx = await baseProgram.methods
        .initPositionPda(hash, sessionKey.publicKey)
        .accounts({
            position: positionPda,
            player: wallet.publicKey,
            systemProgram: SystemProgram.programId,
        } as any)
        .rpc();
    console.log(`Init Tx: ${initTx}`);

    // Step 2: Delegate to MagicBlock
    console.log("\n[2] Delegating PDA to MagicBlock ER...");
    const {
        delegationRecordPdaFromDelegatedAccount,
        delegationMetadataPdaFromDelegatedAccount,
        delegateBufferPdaFromDelegatedAccountAndOwnerProgram,
        DELEGATION_PROGRAM_ID,
    } = require("@magicblock-labs/ephemeral-rollups-sdk");

    const delegateBuffer = delegateBufferPdaFromDelegatedAccountAndOwnerProgram(positionPda, baseProgram.programId);
    const delegationRecord = delegationRecordPdaFromDelegatedAccount(positionPda);
    const delegationMetadata = delegationMetadataPdaFromDelegatedAccount(positionPda);

    const delegateSig = await baseProgram.methods
        .delegatePositionPda()
        .accounts({
            player: wallet.publicKey,
            position: positionPda,
            buffer: delegateBuffer,
            delegationRecord: delegationRecord,
            delegationMetadata: delegationMetadata,
            delegationProgram: DELEGATION_PROGRAM_ID,
            program: baseProgram.programId,
            systemProgram: SystemProgram.programId,
        } as any)
        .rpc({ skipPreflight: true });

    console.log(`Delegate Tx: ${delegateSig}`);

    console.log("Waiting 3 seconds for ER sequencer to sync...");
    await new Promise((resolve) => setTimeout(resolve, 3000));

    // Step 3: Update on MagicBlock
    console.log("\n[3] Sending position updates to MagicBlock Devnet...");
    for (let i = 1; i <= 3; i++) {
        const updateTx = await mbProgram.methods
            .updatePosition(hash, 10.0 * i, 20.0 * i, 50.0, i, i)
            .accounts({
                position: positionPda,
                authority: sessionKey.publicKey,
            } as any)
            .signers([sessionKey])
            .rpc();
        console.log(`Update ${i} (ER Tx): ${updateTx}`);
    }

    const erAccount = await mbProgram.account.playerPosition.fetch(positionPda, "confirmed");
    console.log(`ER State -> X: ${erAccount.x}, Y: ${erAccount.y}, Seq: ${erAccount.seq}`);

    console.log("\nINTEGRATION TEST SUCCESSFUL: Ghost state delegated and updated on MagicBlock ER.");
}

main().catch(err => {
    console.error(err);
    process.exit(1);
});
