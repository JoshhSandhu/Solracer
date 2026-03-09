use anchor_lang::prelude::*;

declare_id!("2g9tQ4g6Qki95UBTGN4NcQ4ggpz5XRa6eQJ8MCuznr8S");

#[program]
pub mod solracer_program {
    use super::*;

    pub fn create_race(
        ctx: Context<CreateRace>,
        race_id: String,
        token_mint: Pubkey,
        entry_fee_sol: u64,
    ) -> Result<()> {
        let race = &mut ctx.accounts.race;
        let clock = Clock::get()?;

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

    pub fn join_race(ctx: Context<JoinRace>) -> Result<()> {
        let race = &mut ctx.accounts.race;

        require!(
            race.status == RaceStatus::Waiting,
            SolracerError::InvalidRaceStatus
        );

        require!(race.player2.is_none(), SolracerError::Player2AlreadySet);

        race.player2 = Some(ctx.accounts.player2.key());
        race.status = RaceStatus::Active;
        race.escrow_amount += race.entry_fee_sol;

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

    /// Create a session key PDA for a player in a specific race.
    /// Called in the same tx as create_race/join_race so only one wallet popup.
    pub fn delegate_session(
        ctx: Context<DelegateSession>,
        race_id_hash: [u8; 32],
        session_key: Pubkey,
        duration_secs: i64,
    ) -> Result<()> {
        let session = &mut ctx.accounts.session;
        session.player_wallet = ctx.accounts.player.key();
        session.session_key = session_key;
        session.race_id_hash = race_id_hash;
        session.expires_at = Clock::get()?.unix_timestamp + duration_secs;
        session.bump = ctx.bumps.session;

        msg!(
            "Session key delegated for player {} in race",
            ctx.accounts.player.key()
        );
        Ok(())
    }

    /// Submit race result accepts either the player wallet directly or a valid session key
    pub fn submit_result(
        ctx: Context<SubmitResult>,
        finish_time_ms: u64,
        coins_collected: u64,
        input_hash: [u8; 32],
    ) -> Result<()> {
        let race = &mut ctx.accounts.race;

        require!(
            race.status == RaceStatus::Active,
            SolracerError::InvalidRaceStatus
        );

        // Resolve the actual player: session key or direct wallet
        let actual_player = if let Some(session) = &ctx.accounts.session {
            require!(
                Clock::get()?.unix_timestamp < session.expires_at,
                SolracerError::SessionExpired
            );
            require!(
                session.session_key == ctx.accounts.authority.key(),
                SolracerError::InvalidSessionKey
            );
            session.player_wallet
        } else {
            ctx.accounts.authority.key()
        };

        let is_player1 = actual_player == race.player1;
        let is_player2 = race
            .player2
            .map(|p2| actual_player == p2)
            .unwrap_or(false);

        require!(is_player1 || is_player2, SolracerError::PlayerNotInRace);

        let result = RaceResult {
            finish_time_ms,
            coins_collected,
            input_hash,
        };

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
            actual_player,
            race.race_id
        );

        Ok(())
    }

    pub fn settle_race(ctx: Context<SettleRace>) -> Result<()> {
        let race = &mut ctx.accounts.race;

        require!(
            race.status == RaceStatus::Active,
            SolracerError::InvalidRaceStatus
        );

        require!(
            race.player1_result.is_some() && race.player2_result.is_some(),
            SolracerError::ResultsNotComplete
        );

        let player1_result = race.player1_result.as_ref().unwrap();
        let player2_result = race.player2_result.as_ref().unwrap();

        let winner = if player1_result.finish_time_ms < player2_result.finish_time_ms {
            race.player1
        } else if player2_result.finish_time_ms < player1_result.finish_time_ms {
            race.player2.unwrap()
        } else {
            if player1_result.coins_collected >= player2_result.coins_collected {
                race.player1
            } else {
                race.player2.unwrap()
            }
        };

        race.winner = Some(winner);
        race.status = RaceStatus::Settled;

        msg!("Race {} settled. Winner: {}", race.race_id, winner);

        Ok(())
    }

    /// Winner claims the prize accepts either the winner wallet directly
    /// or a valid session key funds always go to race.winner
    pub fn claim_prize(ctx: Context<ClaimPrize>) -> Result<()> {
        let race = &mut ctx.accounts.race;

        require!(
            race.status == RaceStatus::Settled,
            SolracerError::InvalidRaceStatus
        );

        // Resolve the actual player: session key or direct wallet
        let actual_player = if let Some(session) = &ctx.accounts.session {
            require!(
                Clock::get()?.unix_timestamp < session.expires_at,
                SolracerError::SessionExpired
            );
            require!(
                session.session_key == ctx.accounts.authority.key(),
                SolracerError::InvalidSessionKey
            );
            session.player_wallet
        } else {
            ctx.accounts.authority.key()
        };

        require!(
            race.winner == Some(actual_player),
            SolracerError::NotWinner
        );

        let prize_amount = race.escrow_amount;

        // Funds go to winner_wallet (the real wallet), not the session key
        **race.to_account_info().try_borrow_mut_lamports()? -= prize_amount;
        **ctx
            .accounts
            .winner_wallet
            .to_account_info()
            .try_borrow_mut_lamports()? += prize_amount;

        race.escrow_amount = 0;

        msg!(
            "Prize of {} lamports claimed by winner {} for race: {}",
            prize_amount,
            actual_player,
            race.race_id
        );

        Ok(())
    }
}

// Accounts

#[account]
pub struct Race {
    pub race_id: String,
    pub token_mint: Pubkey,
    pub entry_fee_sol: u64,
    pub player1: Pubkey,
    pub player2: Option<Pubkey>,
    pub status: RaceStatus,
    pub player1_result: Option<RaceResult>,
    pub player2_result: Option<RaceResult>,
    pub winner: Option<Pubkey>,
    pub escrow_amount: u64,
    pub created_at: i64,
    pub bump: u8,
}

impl Race {
    pub const LEN: usize = 4    // race_id string discriminator
        + 50                    // race_id (max length)
        + 32                    // token_mint pubkey
        + 8                     // entry_fee_sol u64
        + 32                    // player1 pubkey
        + 1 + 32                // player2 option<pubkey>
        + 1                     // status enum
        + 1 + (8 + 8 + 32)     // player1_result option<raceresult>
        + 1 + (8 + 8 + 32)     // player2_result option<raceresult>
        + 1 + 32                // winner option<pubkey>
        + 8                     // escrow_amount u64
        + 8                     // created_at i64
        + 1;                    // bump u8
}

#[account]
pub struct PlayerSession {
    pub player_wallet: Pubkey,   // 32
    pub session_key:   Pubkey,   // 32
    pub race_id_hash:  [u8; 32], // 32
    pub expires_at:    i64,      //  8
    pub bump:          u8,       //  1
}

impl PlayerSession {
    pub const LEN: usize = 105;
}

#[derive(AnchorSerialize, AnchorDeserialize, Clone, Debug)]
pub struct RaceResult {
    pub finish_time_ms: u64,
    pub coins_collected: u64,
    pub input_hash: [u8; 32],
}

#[derive(AnchorSerialize, AnchorDeserialize, Clone, Debug, PartialEq)]
pub enum RaceStatus {
    Waiting,
    Active,
    Settled,
}

// Instruction contexts

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
#[instruction(race_id_hash: [u8; 32], session_key: Pubkey, duration_secs: i64)]
pub struct DelegateSession<'info> {
    #[account(
        init,
        payer = player,
        space = 8 + PlayerSession::LEN,
        seeds = [b"session", race_id_hash.as_ref(), player.key().as_ref()],
        bump
    )]
    pub session: Account<'info, PlayerSession>,

    #[account(mut)]
    pub player: Signer<'info>,

    pub system_program: Program<'info, System>,
}

#[derive(Accounts)]
pub struct SubmitResult<'info> {
    #[account(mut)]
    pub race: Account<'info, Race>,

    /// The signer: either the player wallet or the session key
    pub authority: Signer<'info>,

    /// session PDA, provided when signing with session key.
    #[account(
        seeds = [b"session", session.race_id_hash.as_ref(), player_wallet.key().as_ref()],
        bump = session.bump,
    )]
    pub session: Option<Account<'info, PlayerSession>>,

    /// CHECK: only used for PDA seed derivation when session is provided
    pub player_wallet: UncheckedAccount<'info>,
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

    /// The signer: either the winner wallet or the session key
    pub authority: Signer<'info>,

    /// Optional session PDA, provided when signing with session key
    #[account(
        seeds = [b"session", session.race_id_hash.as_ref(), winner_wallet.key().as_ref()],
        bump = session.bump,
    )]
    pub session: Option<Account<'info, PlayerSession>>,

    /// CHECK: The actual winner wallet that receives funds.
    /// When signing directly (no session), pass the same key as authority.
    #[account(mut)]
    pub winner_wallet: UncheckedAccount<'info>,
}

// Error codes

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
    #[msg("Session key does not match registered key")]
    InvalidSessionKey,
    #[msg("Session has expired")]
    SessionExpired,
}
