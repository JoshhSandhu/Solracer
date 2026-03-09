import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import { SolracerEr } from "../target/types/solracer_er";
import { PublicKey, Keypair, SystemProgram } from "@solana/web3.js";
import { createHash } from "crypto";
import { assert } from "chai";

describe("solracer-er", () => {
    const provider = anchor.AnchorProvider.env();
    anchor.setProvider(provider);

    const program = anchor.workspace.SolracerEr as Program<SolracerEr>;
    const player = provider.wallet as anchor.Wallet;

    function raceIdHash(raceId: string): number[] {
        const hash = createHash("sha256").update(raceId, "utf8").digest();
        return Array.from(hash);
    }

    function derivePositionPda(
        raceId: string,
        playerKey: PublicKey
    ): [PublicKey, number] {
        const hashBytes = Buffer.from(raceIdHash(raceId));
        return PublicKey.findProgramAddressSync(
            [Buffer.from("position"), hashBytes, playerKey.toBuffer()],
            program.programId
        );
    }

    it("init_position_pda creates account with correct fields", async () => {
        const raceId = "550e8400-e29b-41d4-a716-446655440000";
        const sessionKey = Keypair.generate();
        const hash = raceIdHash(raceId);

        const [positionPda] = derivePositionPda(raceId, player.publicKey);

        await program.methods
            .initPositionPda(hash, sessionKey.publicKey)
            .accounts({
                position: positionPda,
                player: player.publicKey,
                systemProgram: SystemProgram.programId,
            })
            .rpc();

        const account = await program.account.playerPosition.fetch(positionPda);
        assert.equal(account.player.toBase58(), player.publicKey.toBase58());
        assert.equal(
            account.sessionKey.toBase58(),
            sessionKey.publicKey.toBase58()
        );
        assert.equal(account.seq, 0);
        assert.deepEqual(Array.from(account.raceIdHash), hash);
    });

    it("update_position writes fields and enforces seq", async () => {
        const raceId = "race-update-test-001";
        const sessionKey = Keypair.generate();
        const hash = raceIdHash(raceId);

        const [positionPda] = derivePositionPda(raceId, player.publicKey);

        await program.methods
            .initPositionPda(hash, sessionKey.publicKey)
            .accounts({
                position: positionPda,
                player: player.publicKey,
                systemProgram: SystemProgram.programId,
            })
            .rpc();

        await program.methods
            .updatePosition(hash, 10.5, 20.3, 55.0, 2, 1)
            .accounts({
                position: positionPda,
                authority: sessionKey.publicKey,
            })
            .signers([sessionKey])
            .rpc();

        const account = await program.account.playerPosition.fetch(positionPda);
        assert.approximately(account.x, 10.5, 0.001);
        assert.approximately(account.y, 20.3, 0.001);
        assert.equal(account.seq, 1);
        assert.equal(account.checkpointIndex, 2);

        try {
            await program.methods
                .updatePosition(hash, 0.0, 0.0, 0.0, 0, 1)
                .accounts({ position: positionPda, authority: sessionKey.publicKey })
                .signers([sessionKey])
                .rpc();
            assert.fail("Expected StaleUpdate error");
        } catch (err: any) {
            assert.include(err.message, "StaleUpdate");
        }
    });

    it("update_position rejects wrong session key", async () => {
        const raceId = "race-auth-test-001";
        const sessionKey = Keypair.generate();
        const badKey = Keypair.generate();
        const hash = raceIdHash(raceId);

        const [positionPda] = derivePositionPda(raceId, player.publicKey);

        await program.methods
            .initPositionPda(hash, sessionKey.publicKey)
            .accounts({
                position: positionPda,
                player: player.publicKey,
                systemProgram: SystemProgram.programId,
            })
            .rpc();

        try {
            await program.methods
                .updatePosition(hash, 1.0, 2.0, 3.0, 0, 1)
                .accounts({ position: positionPda, authority: badKey.publicKey })
                .signers([badKey])
                .rpc();
            assert.fail("Expected InvalidAuthority error");
        } catch (err: any) {
            assert.include(err.message, "InvalidAuthority");
        }
    });

    it("update_position rejects wrong race_id hash", async () => {
        const raceId = "race-hash-test-001";
        const sessionKey = Keypair.generate();
        const correctHash = raceIdHash(raceId);
        const wrongHash = raceIdHash("totally-different-race-999");

        const [positionPda] = derivePositionPda(raceId, player.publicKey);

        await program.methods
            .initPositionPda(correctHash, sessionKey.publicKey)
            .accounts({
                position: positionPda,
                player: player.publicKey,
                systemProgram: SystemProgram.programId,
            })
            .rpc();

        try {
            await program.methods
                .updatePosition(wrongHash, 1.0, 2.0, 3.0, 0, 1)
                .accounts({ position: positionPda, authority: sessionKey.publicKey })
                .signers([sessionKey])
                .rpc();
            assert.fail("Expected RaceIdMismatch error");
        } catch (err: any) {
            assert.include(err.message, "RaceIdMismatch");
        }
    });

    it("close_position_pda closes account and returns rent", async () => {
        const raceId = "race-close-test-001";
        const sessionKey = Keypair.generate();
        const hash = raceIdHash(raceId);

        const [positionPda] = derivePositionPda(raceId, player.publicKey);

        await program.methods
            .initPositionPda(hash, sessionKey.publicKey)
            .accounts({
                position: positionPda,
                player: player.publicKey,
                systemProgram: SystemProgram.programId,
            })
            .rpc();

        await program.methods
            .closePositionPda()
            .accounts({
                position: positionPda,
                player: player.publicKey,
            })
            .rpc();

        const closed = await provider.connection.getAccountInfo(positionPda);
        assert.isNull(closed, "Account should be closed after closePositionPda");
    });
});
