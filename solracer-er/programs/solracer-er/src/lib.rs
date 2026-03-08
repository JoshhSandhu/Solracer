use anchor_lang::prelude::*;
use ephemeral_rollups_sdk::cpi::{delegate_account, DelegateAccounts, DelegateConfig};
use ephemeral_rollups_sdk::anchor::ephemeral;

declare_id!("3BhDmsVJYASHEUE2DJAJr2FHjRWUCF1nwn6SraKJgoEG");

#[ephemeral]
#[program]
pub mod solracer_er {
    use super::*;

    /// Set up a ghost position account for a player entering a race
    /// `race_id_hash` is SHA-256(race_id_utf8) computed client-side and used as a PDA seed
    /// `session_key` is a throwaway keypair generated in Unity that signs all in-race position updates
    pub fn init_position_pda(
        ctx: Context<InitPositionPda>,
        race_id_hash: [u8; 32],
        session_key: Pubkey,
    ) -> Result<()> {
        let pos = &mut ctx.accounts.position;
        pos.race_id_hash = race_id_hash;
        pos.player = ctx.accounts.player.key();
        pos.session_key = session_key;
        pos.x = 0.0;
        pos.y = 0.0;
        pos.speed = 0.0;
        pos.checkpoint_index = 0;
        pos.seq = 0;
        pos.updated_at = Clock::get()?.unix_timestamp;
        pos.bump = ctx.bumps.position;

        msg!(
            "PlayerPosition PDA created for player {}",
            ctx.accounts.player.key(),
        );
        Ok(())
    }

    /// Delegates the PlayerPosition PDA to the MagicBlock er so it can receive low-latency updates
    /// this is called once per race, right after init the PDA can't be written on the er until it's delegated
    pub fn delegate_position_pda(ctx: Context<DelegatePositionPda>) -> Result<()> {
        let pda_signer_seeds: &[&[u8]] = &[
            b"position",
            ctx.accounts.position.race_id_hash.as_ref(),
            ctx.accounts.player.key.as_ref(),
        ];

        let delegate_accounts = DelegateAccounts {
            payer: &ctx.accounts.player.to_account_info(),
            pda: &ctx.accounts.position.to_account_info(),
            owner_program: &ctx.accounts.program.to_account_info(),
            buffer: &ctx.accounts.buffer.to_account_info(),
            delegation_record: &ctx.accounts.delegation_record.to_account_info(),
            delegation_metadata: &ctx.accounts.delegation_metadata.to_account_info(),
            delegation_program: &ctx.accounts.delegation_program.to_account_info(),
            system_program: &ctx.accounts.system_program.to_account_info(),
        };

        delegate_account(
            delegate_accounts,
            pda_signer_seeds,
            DelegateConfig::default(),
        )?;

        msg!("PlayerPosition PDA delegated to MagicBlock er");
        Ok(())
    }

    /// Updates the position snapshot to the er ~300ms
    pub fn update_position(
        ctx: Context<UpdatePosition>,
        expected_race_id_hash: [u8; 32],
        x: f32,
        y: f32,
        speed: f32,
        checkpoint_index: u32,
        seq: u32,
    ) -> Result<()> {
        let pos = &mut ctx.accounts.position;

        require!(
            ctx.accounts.authority.key() == pos.session_key,
            SolracerErError::InvalidAuthority
        );

        require!(
            expected_race_id_hash == pos.race_id_hash,
            SolracerErError::RaceIdMismatch
        );

        require!(seq > pos.seq, SolracerErError::StaleUpdate);

        pos.x = x;
        pos.y = y;
        pos.speed = speed;
        pos.checkpoint_index = checkpoint_index;
        pos.seq = seq;
        pos.updated_at = Clock::get()?.unix_timestamp;

        Ok(())
    }

    /// Closes the position account and returns rent to the player
    /// Called after the race ends and undelegation has settled the final state back to base
    pub fn close_position_pda(_ctx: Context<ClosePositionPda>) -> Result<()> {
        msg!("PlayerPosition PDA closed, rent returned to player");
        Ok(())
    }
}

#[account]
pub struct PlayerPosition {
    pub race_id_hash: [u8; 32],  // 32
    pub player: Pubkey,          // 32
    pub session_key: Pubkey,     // 32
    pub x: f32,                  //  4
    pub y: f32,                  //  4
    pub speed: f32,              //  4
    pub checkpoint_index: u32,   //  4
    pub seq: u32,                //  4
    pub updated_at: i64,         //  8
    pub bump: u8,                //  1
}

impl PlayerPosition {
    // 32 + 32 + 32 + 4 + 4 + 4 + 4 + 4 + 8 + 1 = 125
    pub const LEN: usize = 125;
}

#[derive(Accounts)]
pub struct DelegatePositionPda<'info> {
    #[account(mut)]
    pub player: Signer<'info>,

    #[account(
        mut,
        seeds = [b"position", position.race_id_hash.as_ref(), player.key().as_ref()],
        bump = position.bump,
    )]
    pub position: Account<'info, PlayerPosition>,

    /// CHECK: MagicBlock buffer PDA
    #[account(mut)]
    pub buffer: AccountInfo<'info>,

    /// CHECK: MagicBlock delegation record PDA
    #[account(mut)]
    pub delegation_record: AccountInfo<'info>,

    /// CHECK: MagicBlock delegation metadata PDA
    #[account(mut)]
    pub delegation_metadata: AccountInfo<'info>,

    /// CHECK: MagicBlock delegation program
    #[account(executable)]
    pub delegation_program: AccountInfo<'info>,

    /// CHECK: this program's own ID, used as the owner_program in the delegation CPI
    #[account(address = crate::ID)]
    pub program: AccountInfo<'info>,

    pub system_program: Program<'info, System>,
}

#[derive(Accounts)]
#[instruction(race_id_hash: [u8; 32], session_key: Pubkey)]
pub struct InitPositionPda<'info> {
    #[account(
        init,
        payer = player,
        space = 8 + PlayerPosition::LEN,
        seeds = [
            b"position",
            race_id_hash.as_ref(),
            player.key().as_ref()
        ],
        bump
    )]
    pub position: Account<'info, PlayerPosition>,

    #[account(mut)]
    pub player: Signer<'info>,

    pub system_program: Program<'info, System>,
}

#[derive(Accounts)]
pub struct UpdatePosition<'info> {
    #[account(
        mut,
        seeds = [
            b"position",
            position.race_id_hash.as_ref(),
            position.player.as_ref()
        ],
        bump = position.bump,
    )]
    pub position: Account<'info, PlayerPosition>,

    pub authority: Signer<'info>,
}

#[derive(Accounts)]
pub struct ClosePositionPda<'info> {
    #[account(
        mut,
        close = player,
        has_one = player,
        seeds = [
            b"position",
            position.race_id_hash.as_ref(),
            player.key().as_ref()
        ],
        bump = position.bump,
    )]
    pub position: Account<'info, PlayerPosition>,

    #[account(mut)]
    pub player: Signer<'info>,
}

#[error_code]
pub enum SolracerErError {
    #[msg("Signer is not the registered session key for this PDA")]
    InvalidAuthority,

    #[msg("race_id_hash does not match this PDA, possible cross-race replay")]
    RaceIdMismatch,

    #[msg("Sequence number is not greater than the current value, stale update")]
    StaleUpdate,
}
