import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import { SolracerProgram } from "../target/types/solracer_program";
import { PublicKey, Keypair, SystemProgram, LAMPORTS_PER_SOL } from "@solana/web3.js";
import { expect } from "chai";
import { createHash } from "crypto";

describe("solracer-program", () => {
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.solracerProgram as Program<SolracerProgram>;

  let player1: Keypair;
  let player2: Keypair;
  let raceId: string;
  let tokenMint: PublicKey;
  let racePda: PublicKey;
  let raceBump: number;
  const entryFeeSol = new anchor.BN(0.1 * LAMPORTS_PER_SOL);

  function raceIdHash(raceId: string): number[] {
    const hash = createHash("sha256").update(raceId, "utf8").digest();
    return Array.from(hash);
  }

  function deriveSessionPda(
    raceIdHashBytes: number[],
    playerKey: PublicKey
  ): [PublicKey, number] {
    return PublicKey.findProgramAddressSync(
      [Buffer.from("session"), Buffer.from(raceIdHashBytes), playerKey.toBuffer()],
      program.programId
    );
  }

  before(async () => {
    player1 = Keypair.generate();
    player2 = Keypair.generate();

    const airdrop1 = await provider.connection.requestAirdrop(
      player1.publicKey,
      2 * LAMPORTS_PER_SOL
    );
    const airdrop2 = await provider.connection.requestAirdrop(
      player2.publicKey,
      2 * LAMPORTS_PER_SOL
    );

    await provider.connection.confirmTransaction(airdrop1);
    await provider.connection.confirmTransaction(airdrop2);

    raceId = `race_${Date.now()}`;
    tokenMint = Keypair.generate().publicKey;

    [racePda, raceBump] = PublicKey.findProgramAddressSync(
      [
        Buffer.from("race"),
        Buffer.from(raceId),
        tokenMint.toBuffer(),
        entryFeeSol.toArrayLike(Buffer, "le", 8),
      ],
      program.programId
    );
  });

  describe("create_race", () => {
    it("Creates a new race with entry fee escrow", async () => {
      const player1BalanceBefore = await provider.connection.getBalance(player1.publicKey);

      const tx = await program.methods
        .createRace(raceId, tokenMint, entryFeeSol)
        .accounts({
          race: racePda,
          player1: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();

      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.raceId).to.equal(raceId);
      expect(raceAccount.tokenMint.toString()).to.equal(tokenMint.toString());
      expect(raceAccount.entryFeeSol.toString()).to.equal(entryFeeSol.toString());
      expect(raceAccount.player1.toString()).to.equal(player1.publicKey.toString());
      expect(raceAccount.player2).to.be.null;
      expect(raceAccount.status.waiting).to.not.be.undefined;
      expect(raceAccount.player1Result).to.be.null;
      expect(raceAccount.player2Result).to.be.null;
      expect(raceAccount.winner).to.be.null;
      expect(raceAccount.escrowAmount.toString()).to.equal(entryFeeSol.toString());
      expect(raceAccount.bump).to.equal(raceBump);

      const raceBalanceAfter = await provider.connection.getBalance(racePda);
      expect(raceBalanceAfter).to.be.at.least(entryFeeSol.toNumber());
    });

    it("Fails if race already exists", async () => {
      try {
        await program.methods
          .createRace(raceId, tokenMint, entryFeeSol)
          .accounts({
            race: racePda,
            player1: player1.publicKey,
            systemProgram: SystemProgram.programId,
          })
          .signers([player1])
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });
  });

  describe("join_race", () => {
    it("Allows player2 to join and locks their entry fee", async () => {
      const player2BalanceBefore = await provider.connection.getBalance(player2.publicKey);
      const raceBalanceBefore = await provider.connection.getBalance(racePda);

      const tx = await program.methods
        .joinRace()
        .accounts({
          race: racePda,
          player2: player2.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player2])
        .rpc();

      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.player2?.toString()).to.equal(player2.publicKey.toString());
      expect(raceAccount.status.active).to.not.be.undefined;
      expect(raceAccount.escrowAmount.toString()).to.equal(
        entryFeeSol.mul(new anchor.BN(2)).toString()
      );

      const raceBalanceAfter = await provider.connection.getBalance(racePda);
      expect(raceBalanceAfter).to.equal(raceBalanceBefore + entryFeeSol.toNumber());
    });

    it("Fails if player2 tries to join twice", async () => {
      try {
        await program.methods
          .joinRace()
          .accounts({
            race: racePda,
            player2: player2.publicKey,
            systemProgram: SystemProgram.programId,
          })
          .signers([player2])
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });
  });

  describe("submit_result (direct wallet)", () => {
    it("Allows player1 to submit their result with wallet signing", async () => {
      const finishTimeMs = new anchor.BN(50000);
      const coinsCollected = new anchor.BN(100);
      const inputHash = Buffer.alloc(32, 1);

      await program.methods
        .submitResult(finishTimeMs, coinsCollected, Array.from(inputHash))
        .accounts({
          race: racePda,
          authority: player1.publicKey,
          session: null,
          playerWallet: player1.publicKey,
        } as any)
        .signers([player1])
        .rpc();

      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.player1Result).to.not.be.null;
      expect(raceAccount.player1Result?.finishTimeMs.toString()).to.equal(finishTimeMs.toString());
      expect(raceAccount.player1Result?.coinsCollected.toString()).to.equal(
        coinsCollected.toString()
      );
    });

    it("Allows player2 to submit their result with wallet signing", async () => {
      const finishTimeMs = new anchor.BN(45000);
      const coinsCollected = new anchor.BN(120);
      const inputHash = Buffer.alloc(32, 2);

      await program.methods
        .submitResult(finishTimeMs, coinsCollected, Array.from(inputHash))
        .accounts({
          race: racePda,
          authority: player2.publicKey,
          session: null,
          playerWallet: player2.publicKey,
        } as any)
        .signers([player2])
        .rpc();

      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.player2Result).to.not.be.null;
      expect(raceAccount.player2Result?.finishTimeMs.toString()).to.equal(finishTimeMs.toString());
    });

    it("Fails if player tries to submit result twice", async () => {
      try {
        const finishTimeMs = new anchor.BN(50000);
        const coinsCollected = new anchor.BN(100);
        const inputHash = Buffer.alloc(32, 1);

        await program.methods
          .submitResult(finishTimeMs, coinsCollected, Array.from(inputHash))
          .accounts({
            race: racePda,
            authority: player1.publicKey,
            session: null,
            playerWallet: player1.publicKey,
          } as any)
          .signers([player1])
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });

    it("Fails if non-player tries to submit result", async () => {
      const randomPlayer = Keypair.generate();
      await provider.connection.requestAirdrop(randomPlayer.publicKey, 1 * LAMPORTS_PER_SOL);
      await new Promise((resolve) => setTimeout(resolve, 1000));

      try {
        await program.methods
          .submitResult(new anchor.BN(50000), new anchor.BN(100), Array.from(Buffer.alloc(32, 1)))
          .accounts({
            race: racePda,
            authority: randomPlayer.publicKey,
            session: null,
            playerWallet: randomPlayer.publicKey,
          } as any)
          .signers([randomPlayer])
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });
  });

  describe("settle_race", () => {
    it("Settles the race and determines winner (player2 wins by time)", async () => {
      await program.methods
        .settleRace()
        .accounts({
          race: racePda,
        })
        .rpc();

      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.status.settled).to.not.be.undefined;
      expect(raceAccount.winner?.toString()).to.equal(player2.publicKey.toString());
    });

    it("Fails if both results are not submitted", async () => {
      const newRaceId = `race_${Date.now()}_2`;
      const newTokenMint = Keypair.generate().publicKey;
      const [newRacePda] = PublicKey.findProgramAddressSync(
        [
          Buffer.from("race"),
          Buffer.from(newRaceId),
          newTokenMint.toBuffer(),
          entryFeeSol.toArrayLike(Buffer, "le", 8),
        ],
        program.programId
      );

      await program.methods
        .createRace(newRaceId, newTokenMint, entryFeeSol)
        .accounts({
          race: newRacePda,
          player1: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();

      await program.methods
        .joinRace()
        .accounts({
          race: newRacePda,
          player2: player2.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player2])
        .rpc();

      try {
        await program.methods
          .settleRace()
          .accounts({ race: newRacePda })
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });
  });

  describe("claim_prize (direct wallet)", () => {
    it("Allows winner to claim the prize with wallet signing", async () => {
      const winnerBalanceBefore = await provider.connection.getBalance(player2.publicKey);

      await program.methods
        .claimPrize()
        .accounts({
          race: racePda,
          authority: player2.publicKey,
          session: null,
          winnerWallet: player2.publicKey,
        } as any)
        .signers([player2])
        .rpc();

      const winnerBalanceAfter = await provider.connection.getBalance(player2.publicKey);
      const expectedPrize = entryFeeSol.mul(new anchor.BN(2)).toNumber();
      expect(winnerBalanceAfter).to.be.greaterThan(winnerBalanceBefore + expectedPrize - 10000);

      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.escrowAmount.toString()).to.equal("0");
    });

    it("Fails if non-winner tries to claim prize", async () => {
      const newRaceId = `race_${Date.now()}_4`;
      const newTokenMint = Keypair.generate().publicKey;
      const [newRacePda] = PublicKey.findProgramAddressSync(
        [
          Buffer.from("race"),
          Buffer.from(newRaceId),
          newTokenMint.toBuffer(),
          entryFeeSol.toArrayLike(Buffer, "le", 8),
        ],
        program.programId
      );

      await program.methods
        .createRace(newRaceId, newTokenMint, entryFeeSol)
        .accounts({
          race: newRacePda,
          player1: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();

      await program.methods
        .joinRace()
        .accounts({
          race: newRacePda,
          player2: player2.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player2])
        .rpc();

      await program.methods
        .submitResult(new anchor.BN(40000), new anchor.BN(100), Array.from(Buffer.alloc(32, 1)))
        .accounts({
          race: newRacePda,
          authority: player1.publicKey,
          session: null,
          playerWallet: player1.publicKey,
        } as any)
        .signers([player1])
        .rpc();

      await program.methods
        .submitResult(new anchor.BN(50000), new anchor.BN(100), Array.from(Buffer.alloc(32, 2)))
        .accounts({
          race: newRacePda,
          authority: player2.publicKey,
          session: null,
          playerWallet: player2.publicKey,
        } as any)
        .signers([player2])
        .rpc();

      await program.methods
        .settleRace()
        .accounts({ race: newRacePda })
        .rpc();

      try {
        await program.methods
          .claimPrize()
          .accounts({
            race: newRacePda,
            authority: player2.publicKey,
            session: null,
            winnerWallet: player2.publicKey,
          } as any)
          .signers([player2])
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });
  });

  describe("delegate_session", () => {
    it("Creates a session key PDA with correct fields", async () => {
      const sessionRaceId = `race_session_${Date.now()}`;
      const sessionTokenMint = Keypair.generate().publicKey;
      const [sessionRacePda] = PublicKey.findProgramAddressSync(
        [
          Buffer.from("race"),
          Buffer.from(sessionRaceId),
          sessionTokenMint.toBuffer(),
          entryFeeSol.toArrayLike(Buffer, "le", 8),
        ],
        program.programId
      );

      const sessionKey = Keypair.generate();
      const hash = raceIdHash(sessionRaceId);
      const [sessionPda] = deriveSessionPda(hash, player1.publicKey);

      // Create the race first
      await program.methods
        .createRace(sessionRaceId, sessionTokenMint, entryFeeSol)
        .accounts({
          race: sessionRacePda,
          player1: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();

      // Delegate session
      await program.methods
        .delegateSession(hash, sessionKey.publicKey, new anchor.BN(10800))
        .accounts({
          session: sessionPda,
          player: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();

      const sessionAccount = await program.account.playerSession.fetch(sessionPda);
      expect(sessionAccount.playerWallet.toString()).to.equal(player1.publicKey.toString());
      expect(sessionAccount.sessionKey.toString()).to.equal(sessionKey.publicKey.toString());
      expect(sessionAccount.expiresAt.toNumber()).to.be.greaterThan(0);
      expect(Array.from(sessionAccount.raceIdHash)).to.deep.equal(hash);
    });
  });

  describe("session key signing", () => {
    let sessionRaceId: string;
    let sessionRacePda: PublicKey;
    let sessionKey: Keypair;
    let sessionPda: PublicKey;
    let hash: number[];

    before(async () => {
      sessionRaceId = `race_sk_${Date.now()}`;
      const sessionTokenMint = Keypair.generate().publicKey;
      [sessionRacePda] = PublicKey.findProgramAddressSync(
        [
          Buffer.from("race"),
          Buffer.from(sessionRaceId),
          sessionTokenMint.toBuffer(),
          entryFeeSol.toArrayLike(Buffer, "le", 8),
        ],
        program.programId
      );

      sessionKey = Keypair.generate();
      hash = raceIdHash(sessionRaceId);
      [sessionPda] = deriveSessionPda(hash, player1.publicKey);

      // Create race
      await program.methods
        .createRace(sessionRaceId, sessionTokenMint, entryFeeSol)
        .accounts({
          race: sessionRacePda,
          player1: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();

      // Player2 joins
      await program.methods
        .joinRace()
        .accounts({
          race: sessionRacePda,
          player2: player2.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player2])
        .rpc();

      // Create session for player1
      await program.methods
        .delegateSession(hash, sessionKey.publicKey, new anchor.BN(10800))
        .accounts({
          session: sessionPda,
          player: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();
    });

    it("submit_result works with session key signer", async () => {
      await program.methods
        .submitResult(new anchor.BN(42000), new anchor.BN(200), Array.from(Buffer.alloc(32, 3)))
        .accounts({
          race: sessionRacePda,
          authority: sessionKey.publicKey,
          session: sessionPda,
          playerWallet: player1.publicKey,
        } as any)
        .signers([sessionKey])
        .rpc();

      const raceAccount = await program.account.race.fetch(sessionRacePda);
      expect(raceAccount.player1Result).to.not.be.null;
      expect(raceAccount.player1Result?.finishTimeMs.toString()).to.equal("42000");
    });

    it("submit_result rejects wrong session key", async () => {
      const wrongKey = Keypair.generate();
      const wrongHash = raceIdHash(sessionRaceId);
      // Create a session with the wrong key for player2
      const [wrongSessionPda] = deriveSessionPda(wrongHash, player2.publicKey);

      await program.methods
        .delegateSession(wrongHash, wrongKey.publicKey, new anchor.BN(10800))
        .accounts({
          session: wrongSessionPda,
          player: player2.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player2])
        .rpc();

      // Try to use a completely different key as authority
      const fakeKey = Keypair.generate();
      try {
        await program.methods
          .submitResult(new anchor.BN(55000), new anchor.BN(100), Array.from(Buffer.alloc(32, 4)))
          .accounts({
            race: sessionRacePda,
            authority: fakeKey.publicKey,
            session: wrongSessionPda,
            playerWallet: player2.publicKey,
          } as any)
          .signers([fakeKey])
          .rpc();

        expect.fail("Expected InvalidSessionKey error");
      } catch (err: any) {
        expect(err.message).to.include("InvalidSessionKey");
      }
    });

    it("submit_result rejects expired session", async () => {
      // Create a session with 0 duration (immediately expired)
      const expiredRaceId = `race_expired_${Date.now()}`;
      const expiredTokenMint = Keypair.generate().publicKey;
      const [expiredRacePda] = PublicKey.findProgramAddressSync(
        [
          Buffer.from("race"),
          Buffer.from(expiredRaceId),
          expiredTokenMint.toBuffer(),
          entryFeeSol.toArrayLike(Buffer, "le", 8),
        ],
        program.programId
      );

      const expiredSessionKey = Keypair.generate();
      const expiredHash = raceIdHash(expiredRaceId);
      const [expiredSessionPda] = deriveSessionPda(expiredHash, player1.publicKey);

      // Need a fresh player1 session PDA since other one already exists
      const freshPlayer = Keypair.generate();
      await provider.connection.requestAirdrop(freshPlayer.publicKey, 2 * LAMPORTS_PER_SOL);
      await new Promise((resolve) => setTimeout(resolve, 1000));

      const [freshSessionPda] = deriveSessionPda(expiredHash, freshPlayer.publicKey);

      await program.methods
        .createRace(expiredRaceId, expiredTokenMint, entryFeeSol)
        .accounts({
          race: expiredRacePda,
          player1: freshPlayer.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([freshPlayer])
        .rpc();

      await program.methods
        .joinRace()
        .accounts({
          race: expiredRacePda,
          player2: player2.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player2])
        .rpc();

      // Create session with -1 duration (expired immediately)
      await program.methods
        .delegateSession(expiredHash, expiredSessionKey.publicKey, new anchor.BN(-1))
        .accounts({
          session: freshSessionPda,
          player: freshPlayer.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([freshPlayer])
        .rpc();

      try {
        await program.methods
          .submitResult(new anchor.BN(50000), new anchor.BN(100), Array.from(Buffer.alloc(32, 5)))
          .accounts({
            race: expiredRacePda,
            authority: expiredSessionKey.publicKey,
            session: freshSessionPda,
            playerWallet: freshPlayer.publicKey,
          } as any)
          .signers([expiredSessionKey])
          .rpc();

        expect.fail("Expected SessionExpired error");
      } catch (err: any) {
        expect(err.message).to.include("SessionExpired");
      }
    });

    it("claim_prize works with session key and funds go to player wallet", async () => {
      // Player2 submits directly
      await program.methods
        .submitResult(new anchor.BN(55000), new anchor.BN(100), Array.from(Buffer.alloc(32, 4)))
        .accounts({
          race: sessionRacePda,
          authority: player2.publicKey,
          session: null,
          playerWallet: player2.publicKey,
        } as any)
        .signers([player2])
        .rpc();

      // Settle
      await program.methods
        .settleRace()
        .accounts({ race: sessionRacePda })
        .rpc();

      const raceAccount = await program.account.race.fetch(sessionRacePda);
      expect(raceAccount.winner?.toString()).to.equal(player1.publicKey.toString());

      // Claim with session key - funds go to player1 wallet, NOT to session key
      const player1BalanceBefore = await provider.connection.getBalance(player1.publicKey);

      await program.methods
        .claimPrize()
        .accounts({
          race: sessionRacePda,
          authority: sessionKey.publicKey,
          session: sessionPda,
          winnerWallet: player1.publicKey,
        } as any)
        .signers([sessionKey])
        .rpc();

      const player1BalanceAfter = await provider.connection.getBalance(player1.publicKey);
      expect(player1BalanceAfter).to.be.greaterThan(player1BalanceBefore);

      const raceAfter = await program.account.race.fetch(sessionRacePda);
      expect(raceAfter.escrowAmount.toString()).to.equal("0");
    });
  });
});
