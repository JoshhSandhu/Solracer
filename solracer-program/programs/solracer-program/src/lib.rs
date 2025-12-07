use anchor_lang::prelude::*;

declare_id!("5Qe7B4LEMjmfbWgt2ctKY8ZzesDobubBi79HwPABJFkQ");

#[program]
pub mod solracer_program {
    use super::*;

    //init a new race with entry fee escrow
    pub fn create_race(
        ctx: Context<CreateRace>,
        race_id: String,
        token_mint: Pubkey,
        entry_fee_sol: u64,
    ) -> Result<()> {
        let race = &mut ctx.accounts.race;
        let clock = Clock::get()?;

        //init race state
        race.race_id = race_id.clone();
        race.token_mint = token_mint;
        race.entry_fee_sol = entry_fee_sol;
        race.player1 = ctx.accounts.player1.key();
        race.player2 = None;
        race.status = RaceStatus::Waiting;
        race.player1_result = None;
        race.player2_result = None;
        race.winner = None;
        race.escrow_amount = entry_fee_sol;
        race.created_at = clock.unix_timestamp;
        race.bump = ctx.bumps.race;

        //transfer entry fee from player1 to race PDA escrow
        anchor_lang::solana_program::program::invoke(
            &anchor_lang::solana_program::system_instruction::transfer(
                &ctx.accounts.player1.key(),
                &race.key(),
                entry_fee_sol,
            ),
            &[
                ctx.accounts.player1.to_account_info(),
                race.to_account_info(),
                ctx.accounts.system_program.to_account_info(),
            ],
        )?;

        msg!(
            "Race created: {} by player1: {} with entry fee: {} lamports",
            race_id,
            ctx.accounts.player1.key(),
            entry_fee_sol
        );

        Ok(())
    }

    //player 2 joins the race and locks their entry fee
    pub fn join_race(ctx: Context<JoinRace>) -> Result<()> {
        let race = &mut ctx.accounts.race;

        //verify race is in waiting status
        require!(
            race.status == RaceStatus::Waiting,
            SolracerError::InvalidRaceStatus
        );

        //verify player2 is not already set
        require!(race.player2.is_none(), SolracerError::Player2AlreadySet);

        //set player2
        race.player2 = Some(ctx.accounts.player2.key());
        race.status = RaceStatus::Active;
        race.escrow_amount += race.entry_fee_sol;

        //transfer entry fee from player2 to race PDA escrow
        anchor_lang::solana_program::program::invoke(
            &anchor_lang::solana_program::system_instruction::transfer(
                &ctx.accounts.player2.key(),
                &race.key(),
                race.entry_fee_sol,
            ),
            &[
                ctx.accounts.player2.to_account_info(),
                race.to_account_info(),
                ctx.accounts.system_program.to_account_info(),
            ],
        )?;

        msg!(
            "Player2 {} joined race: {}",
            ctx.accounts.player2.key(),
            race.race_id
        );

        Ok(())
    }

    //submit race result for a player
    pub fn submit_result(
        ctx: Context<SubmitResult>,
        finish_time_ms: u64,
        coins_collected: u64,
        input_hash: [u8; 32],
    ) -> Result<()> {
        let race = &mut ctx.accounts.race;

        //verify race is active
        require!(
            race.status == RaceStatus::Active,
            SolracerError::InvalidRaceStatus
        );

        //verify player is in the race
        let is_player1 = ctx.accounts.player.key() == race.player1;
        let is_player2 = race
            .player2
            .map(|p2| ctx.accounts.player.key() == p2)
            .unwrap_or(false);

        require!(is_player1 || is_player2, SolracerError::PlayerNotInRace);

        //create result
        let result = RaceResult {
            finish_time_ms,
            coins_collected,
            input_hash,
        };

        //store result based on player
        if is_player1 {
            require!(
                race.player1_result.is_none(),
                SolracerError::ResultAlreadySubmitted
            );
            race.player1_result = Some(result);
        } else {
            require!(
                race.player2_result.is_none(),
                SolracerError::ResultAlreadySubmitted
            );
            race.player2_result = Some(result);
        }

        msg!(
            "Result submitted for player {} in race: {}",
            ctx.accounts.player.key(),
            race.race_id
        );

        Ok(())
    }

    //settle the race and determine the winner
    pub fn settle_race(ctx: Context<SettleRace>) -> Result<()> {
        let race = &mut ctx.accounts.race;

        //verify race is active
        require!(
            race.status == RaceStatus::Active,
            SolracerError::InvalidRaceStatus
        );

        //verify both results are submitted
        require!(
            race.player1_result.is_some() && race.player2_result.is_some(),
            SolracerError::ResultsNotComplete
        );

        let player1_result = race.player1_result.as_ref().unwrap();
        let player2_result = race.player2_result.as_ref().unwrap();

        //determine winner based on finish time (lower is better)
        //if times are equal, use coins collected (higher is better)
        let winner = if player1_result.finish_time_ms < player2_result.finish_time_ms {
            race.player1
        } else if player2_result.finish_time_ms < player1_result.finish_time_ms {
            race.player2.unwrap()
        } else {
            //tie on time, use coins collected
            if player1_result.coins_collected >= player2_result.coins_collected {
                race.player1
            } else {
                race.player2.unwrap()
            }
        };

        race.winner = Some(winner);
        race.status = RaceStatus::Settled;

        msg!(
            "Race {} settled. Winner: {}",
            race.race_id,
            winner
        );

        Ok(())
    }

    //winner claims the prize from escrow
    pub fn claim_prize(ctx: Context<ClaimPrize>) -> Result<()> {
        let race = &mut ctx.accounts.race;

        //verify race is settled
        require!(
            race.status == RaceStatus::Settled,
            SolracerError::InvalidRaceStatus
        );

        //verify caller is the winner
        require!(
            race.winner == Some(ctx.accounts.winner.key()),
            SolracerError::NotWinner
        );

        //calculate prize (total escrow amount)
        let prize_amount = race.escrow_amount;

        //transfer prize from race PDA to winner
        **race.to_account_info().try_borrow_mut_lamports()? -= prize_amount;
        **ctx
            .accounts
            .winner
            .to_account_info()
            .try_borrow_mut_lamports()? += prize_amount;

        //mark escrow as claimed
        race.escrow_amount = 0;

        msg!(
            "Prize of {} lamports claimed by winner {} for race: {}",
            prize_amount,
            ctx.accounts.winner.key(),
            race.race_id
        );

        Ok(())
    }
}

//account structures
#[account]
pub struct Race {
    pub race_id: String,                    //deterministic race ID
    pub token_mint: Pubkey,                 //token for race
    pub entry_fee_sol: u64,                 //entry fee in lamports
    pub player1: Pubkey,                    //player 1 wallet
    pub player2: Option<Pubkey>,            //player 2 wallet (optional)
    pub status: RaceStatus,                  //waiting, active, settled
    pub player1_result: Option<RaceResult>, //player 1 result
    pub player2_result: Option<RaceResult>, //player 2 result
    pub winner: Option<Pubkey>,              //winner wallet
    pub escrow_amount: u64,                 //total escrowed SOL
    pub created_at: i64,                    //timestamp
    pub bump: u8,                           //pda bump
}


//data structures
#[derive(AnchorSerialize, AnchorDeserialize, Clone, Debug)]
pub struct RaceResult {
    pub finish_time_ms: u64,    //finish time in milliseconds
    pub coins_collected: u64,   //coins collected
    pub input_hash: [u8; 32],   //sha256 hash of input
}

#[derive(AnchorSerialize, AnchorDeserialize, Clone, Debug, PartialEq)]
pub enum RaceStatus {
    Waiting,  //waiting for player2
    Active,   //both players joined, race in progress
    Settled,  //race settled, winner determined
}

//context structures
#[derive(Accounts)]
#[instruction(race_id: String, token_mint: Pubkey, entry_fee_sol: u64)]
pub struct CreateRace<'info> {
    #[account(
        init,
        payer = player1,
        space = 8 + Race::LEN,
        seeds = [b"race", race_id.as_bytes(), token_mint.as_ref(), &entry_fee_sol.to_le_bytes()],
        bump
    )]
    pub race: Account<'info, Race>,

    #[account(mut)]
    pub player1: Signer<'info>,

    pub system_program: Program<'info, System>,
}

#[derive(Accounts)]
pub struct JoinRace<'info> {
    #[account(mut)]
    pub race: Account<'info, Race>,

    #[account(mut)]
    pub player2: Signer<'info>,

    pub system_program: Program<'info, System>,
}

#[derive(Accounts)]
pub struct SubmitResult<'info> {
    #[account(mut)]
    pub race: Account<'info, Race>,

    pub player: Signer<'info>,
}

#[derive(Accounts)]
pub struct SettleRace<'info> {
    #[account(mut)]
    pub race: Account<'info, Race>,
}

#[derive(Accounts)]
pub struct ClaimPrize<'info> {
    #[account(mut)]
    pub race: Account<'info, Race>,

    #[account(mut)]
    pub winner: Signer<'info>,
}


//error codes
#[error_code]
pub enum SolracerError {
    #[msg("Invalid race status for this operation")]
    InvalidRaceStatus,
    #[msg("Player2 is already set")]
    Player2AlreadySet,
    #[msg("Player is not in this race")]
    PlayerNotInRace,
    #[msg("Result already submitted for this player")]
    ResultAlreadySubmitted,
    #[msg("Both results must be submitted before settling")]
    ResultsNotComplete,
    #[msg("Only the winner can claim the prize")]
    NotWinner,
}


//constants
impl Race {
    pub const LEN: usize = 4    //race_id string discriminator    
        + 50                    //race_id (max length)
        + 32                    //token_mint pubkey
        + 8                     //entry_fee_sol u64
        + 32                    //player1 pubkey
        + 1 + 32                //player2 option<pubkey>
        + 1                     //status enum
        + 1 + (8 + 8 + 32)      //player1_result option<raceresult>
        + 1 + (8 + 8 + 32)      //player2_result option<raceresult>
        + 1 + 32                //winner option<pubkey>
        + 8                     //escrow_amount u64
        + 8                     //created_at i64
        + 1;                    //bump u8
}
