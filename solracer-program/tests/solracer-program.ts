import * as anchor from "@coral-xyz/anchor";
import { Program } from "@coral-xyz/anchor";
import { SolracerProgram } from "../target/types/solracer_program";
import { PublicKey, Keypair, SystemProgram, LAMPORTS_PER_SOL } from "@solana/web3.js";
import { expect } from "chai";

describe("solracer-program", () => {
  //configure the client to use the local cluster
  const provider = anchor.AnchorProvider.env();
  anchor.setProvider(provider);

  const program = anchor.workspace.solracerProgram as Program<SolracerProgram>;

  //test accounts
  let player1: Keypair;
  let player2: Keypair;
  let raceId: string;
  let tokenMint: PublicKey;
  let racePda: PublicKey;
  let raceBump: number;
  const entryFeeSol = new anchor.BN(0.1 * LAMPORTS_PER_SOL); // 0.1 SOL

  before(async () => {
    //generate test keypairs
    player1 = Keypair.generate();
    player2 = Keypair.generate();

    //airdrop SOL to test accounts
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

    //generate test race ID and token mint
    raceId = `race_${Date.now()}`;
    tokenMint = Keypair.generate().publicKey;

    //derive race PDA
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
      const raceBalanceBefore = await provider.connection.getBalance(racePda);

      const tx = await program.methods
        .createRace(raceId, tokenMint, entryFeeSol)
        .accounts({
          race: racePda,
          player1: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();

      console.log("Create race transaction signature:", tx);

      //verify race account was created
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

      //verify SOL was transferred to escrow
      const player1BalanceAfter = await provider.connection.getBalance(player1.publicKey);
      const raceBalanceAfter = await provider.connection.getBalance(racePda);

      //account for transaction fees (roughly)
      const expectedPlayer1Balance = player1BalanceBefore - entryFeeSol.toNumber() - 5000;
      expect(player1BalanceAfter).to.be.lessThan(expectedPlayer1Balance + 10000);
      //race account balance = entry fee + rent exemption
      //verify the balance is at least the entry fee
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

      console.log("Join race transaction signature:", tx);

      //verify race account was updated
      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.player2?.toString()).to.equal(player2.publicKey.toString());
      expect(raceAccount.status.active).to.not.be.undefined;
      expect(raceAccount.escrowAmount.toString()).to.equal(
        entryFeeSol.mul(new anchor.BN(2)).toString()
      );

      //verify SOL was transferred to escrow
      const player2BalanceAfter = await provider.connection.getBalance(player2.publicKey);
      const raceBalanceAfter = await provider.connection.getBalance(racePda);

      const expectedPlayer2Balance = player2BalanceBefore - entryFeeSol.toNumber() - 5000;
      expect(player2BalanceAfter).to.be.lessThan(expectedPlayer2Balance + 10000);
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

    it("Fails if race is not in waiting status", async () => {
      //create a new race for this test
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

      //create race
      await program.methods
        .createRace(newRaceId, newTokenMint, entryFeeSol)
        .accounts({
          race: newRacePda,
          player1: player1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player1])
        .rpc();

      //join race
      await program.methods
        .joinRace()
        .accounts({
          race: newRacePda,
          player2: player2.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([player2])
        .rpc();

      //try to join again (should fail)
      try {
        await program.methods
          .joinRace()
          .accounts({
            race: newRacePda,
            player2: Keypair.generate().publicKey,
            systemProgram: SystemProgram.programId,
          })
          .signers([Keypair.generate()])
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });
  });

  describe("submit_result", () => {
    it("Allows player1 to submit their result", async () => {
      const finishTimeMs = new anchor.BN(50000); // 50 seconds
      const coinsCollected = new anchor.BN(100);
      const inputHash = Buffer.alloc(32, 1); // Dummy hash

      const tx = await program.methods
        .submitResult(finishTimeMs, coinsCollected, Array.from(inputHash))
        .accounts({
          race: racePda,
          player: player1.publicKey,
        })
        .signers([player1])
        .rpc();

      console.log("Submit result transaction signature:", tx);

      //verify result was stored
      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.player1Result).to.not.be.null;
      expect(raceAccount.player1Result?.finishTimeMs.toString()).to.equal(finishTimeMs.toString());
      expect(raceAccount.player1Result?.coinsCollected.toString()).to.equal(
        coinsCollected.toString()
      );
    });

    it("Allows player2 to submit their result", async () => {
      const finishTimeMs = new anchor.BN(45000); // 45 seconds (faster)
      const coinsCollected = new anchor.BN(120);
      const inputHash = Buffer.alloc(32, 2); // Dummy hash

      const tx = await program.methods
        .submitResult(finishTimeMs, coinsCollected, Array.from(inputHash))
        .accounts({
          race: racePda,
          player: player2.publicKey,
        })
        .signers([player2])
        .rpc();

      console.log("Submit result transaction signature:", tx);

        //verify result was stored
      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.player2Result).to.not.be.null;
      expect(raceAccount.player2Result?.finishTimeMs.toString()).to.equal(finishTimeMs.toString());
      expect(raceAccount.player2Result?.coinsCollected.toString()).to.equal(
        coinsCollected.toString()
      );
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
            player: player1.publicKey,
          })
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
        const finishTimeMs = new anchor.BN(50000);
        const coinsCollected = new anchor.BN(100);
        const inputHash = Buffer.alloc(32, 1);

        await program.methods
          .submitResult(finishTimeMs, coinsCollected, Array.from(inputHash))
          .accounts({
            race: racePda,
            player: randomPlayer.publicKey,
          })
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
      const tx = await program.methods
        .settleRace()
        .accounts({
          race: racePda,
        })
        .rpc();

      console.log("Settle race transaction signature:", tx);

      //verify race was settled
      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.status.settled).to.not.be.undefined;
      expect(raceAccount.winner?.toString()).to.equal(player2.publicKey.toString());
    });

    it("Fails if both results are not submitted", async () => {
      //create a new race for this test
      const newRaceId = `race_${Date.now()}_3`;
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

      //create and join race
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

      //try to settle without both results (should fail)
      try {
        await program.methods
          .settleRace()
          .accounts({
            race: newRacePda,
          })
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });
  });

  describe("claim_prize", () => {
    it("Allows winner to claim the prize", async () => {
      const winnerBalanceBefore = await provider.connection.getBalance(player2.publicKey);
      const raceBalanceBefore = await provider.connection.getBalance(racePda);

      const tx = await program.methods
        .claimPrize()
        .accounts({
          race: racePda,
          winner: player2.publicKey,
        })
        .signers([player2])
        .rpc();

      console.log("Claim prize transaction signature:", tx);

      //verify prize was transferred
      const winnerBalanceAfter = await provider.connection.getBalance(player2.publicKey);
      const raceBalanceAfter = await provider.connection.getBalance(racePda);

      //winner should receive the escrow amount (-transaction fees)
      const expectedPrize = entryFeeSol.mul(new anchor.BN(2)).toNumber();
      expect(winnerBalanceAfter).to.be.greaterThan(winnerBalanceBefore + expectedPrize - 10000);
      expect(raceBalanceAfter).to.be.lessThan(raceBalanceBefore - expectedPrize + 10000);

      //verify escrow amount was reset
      const raceAccount = await program.account.race.fetch(racePda);
      expect(raceAccount.escrowAmount.toString()).to.equal("0");
    });

    it("Fails if non-winner tries to claim prize", async () => {
      //create a new race for this test
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

      //create and join race
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

      //submit results (player1 wins this time)
      await program.methods
        .submitResult(
          new anchor.BN(40000), //player1 faster
          new anchor.BN(100),
          Array.from(Buffer.alloc(32, 1))
        )
        .accounts({
          race: newRacePda,
          player: player1.publicKey,
        })
        .signers([player1])
        .rpc();

      await program.methods
        .submitResult(
          new anchor.BN(50000), //player2 slower
          new anchor.BN(100),
          Array.from(Buffer.alloc(32, 2))
        )
        .accounts({
          race: newRacePda,
          player: player2.publicKey,
        })
        .signers([player2])
        .rpc();

      //settle race
      await program.methods
        .settleRace()
        .accounts({
          race: newRacePda,
        })
        .rpc();

      //try to claim with wrong player (should fail)
      try {
        await program.methods
          .claimPrize()
          .accounts({
            race: newRacePda,
            winner: player2.publicKey, //player2 is not the winner
          })
          .signers([player2])
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });

    it("Fails if race is not settled", async () => {
      //create a new race for this test
      const newRaceId = `race_${Date.now()}_5`;
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

      //create and join race
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

      //try to claim before settling (should fail)
      try {
        await program.methods
          .claimPrize()
          .accounts({
            race: newRacePda,
            winner: player1.publicKey,
          })
          .signers([player1])
          .rpc();

        expect.fail("Should have thrown an error");
      } catch (err) {
        expect(err).to.not.be.null;
      }
    });
  });

  describe("Full race flow", () => {
    it("Completes a full race from creation to prize claim", async () => {
      const fullRaceId = `race_full_${Date.now()}`;
      const fullTokenMint = Keypair.generate().publicKey;
      const [fullRacePda] = PublicKey.findProgramAddressSync(
        [
          Buffer.from("race"),
          Buffer.from(fullRaceId),
          fullTokenMint.toBuffer(),
          entryFeeSol.toArrayLike(Buffer, "le", 8),
        ],
        program.programId
      );

      const testPlayer1 = Keypair.generate();
      const testPlayer2 = Keypair.generate();

      //airdrop SOL
      await provider.connection.requestAirdrop(testPlayer1.publicKey, 2 * LAMPORTS_PER_SOL);
      await provider.connection.requestAirdrop(testPlayer2.publicKey, 2 * LAMPORTS_PER_SOL);
      await new Promise((resolve) => setTimeout(resolve, 1000));

      //create race
      await program.methods
        .createRace(fullRaceId, fullTokenMint, entryFeeSol)
        .accounts({
          race: fullRacePda,
          player1: testPlayer1.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([testPlayer1])
        .rpc();

      let raceAccount = await program.account.race.fetch(fullRacePda);
      expect(raceAccount.status.waiting).to.not.be.undefined;

      //join race
      await program.methods
        .joinRace()
        .accounts({
          race: fullRacePda,
          player2: testPlayer2.publicKey,
          systemProgram: SystemProgram.programId,
        })
        .signers([testPlayer2])
        .rpc();

      raceAccount = await program.account.race.fetch(fullRacePda);
      expect(raceAccount.status.active).to.not.be.undefined;

      //submit results
      await program.methods
        .submitResult(new anchor.BN(30000), new anchor.BN(150), Array.from(Buffer.alloc(32, 1)))
        .accounts({
          race: fullRacePda,
          player: testPlayer1.publicKey,
        })
        .signers([testPlayer1])
        .rpc();

      await program.methods
        .submitResult(new anchor.BN(35000), new anchor.BN(140), Array.from(Buffer.alloc(32, 2)))
        .accounts({
          race: fullRacePda,
          player: testPlayer2.publicKey,
        })
        .signers([testPlayer2])
        .rpc();

      raceAccount = await program.account.race.fetch(fullRacePda);
      expect(raceAccount.player1Result).to.not.be.null;
      expect(raceAccount.player2Result).to.not.be.null;

      //settle race
      await program.methods
        .settleRace()
        .accounts({
          race: fullRacePda,
        })
        .rpc();

      raceAccount = await program.account.race.fetch(fullRacePda);
      expect(raceAccount.status.settled).to.not.be.undefined;
      expect(raceAccount.winner?.toString()).to.equal(testPlayer1.publicKey.toString());

      //claim prize
      const winnerBalanceBefore = await provider.connection.getBalance(testPlayer1.publicKey);
      await program.methods
        .claimPrize()
        .accounts({
          race: fullRacePda,
          winner: testPlayer1.publicKey,
        })
        .signers([testPlayer1])
        .rpc();

      const winnerBalanceAfter = await provider.connection.getBalance(testPlayer1.publicKey);
      expect(winnerBalanceAfter).to.be.greaterThan(winnerBalanceBefore);

      raceAccount = await program.account.race.fetch(fullRacePda);
      expect(raceAccount.escrowAmount.toString()).to.equal("0");
    });
  });
});
