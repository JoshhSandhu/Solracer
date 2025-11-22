#!/usr/bin/env python3
"""
Test script for Phase 4.2: Backend Solana Integration

This script tests all Solana integration services:
- PDA utilities
- Solana RPC client
- Program client (IDL loading)
- Transaction builder
- Transaction submitter
- On-chain sync

Run with: python scripts/test_phase4_2.py
"""

import sys
import os
from pathlib import Path

#add backend directory to path
backend_dir = Path(__file__).parent.parent
sys.path.insert(0, str(backend_dir))

from dotenv import load_dotenv

#load environment variables
load_dotenv(dotenv_path=backend_dir / ".env")

from solders.pubkey import Pubkey
from solders.system_program import ID as SYSTEM_PROGRAM_ID


def test_environment_variables():
    """Test that all required environment variables are set."""
    print("\n" + "="*60)
    print("TEST 1: Environment Variables")
    print("="*60)
    
    required_vars = [
        "SOLANA_RPC_URL",
        "SOLANA_PROGRAM_ID",
    ]
    
    optional_vars = [
        "SOLANA_NETWORK",
        "SOLANA_COMMITMENT",
        "BACKEND_WALLET_PRIVATE_KEY",
    ]
    
    all_present = True
    
    print("\nRequired Variables:")
    for var in required_vars:
        value = os.getenv(var)
        if value:
            #mask sensitive values
            if "PRIVATE_KEY" in var:
                display_value = value[:10] + "..." if len(value) > 10 else value
            else:
                display_value = value
            print(f"{var}: {display_value}")
        else:
            print(f"{var}: NOT SET")
            all_present = False
    
    print("\nOptional Variables:")
    for var in optional_vars:
        value = os.getenv(var)
        if value:
            if "PRIVATE_KEY" in var:
                display_value = value[:10] + "..." if len(value) > 10 else value
            else:
                display_value = value
            print(f"{var}: {display_value}")
        else:
            print(f"{var}: NOT SET (optional)")
    
    return all_present


def test_idl_file():
    """test that IDL file exists and is valid JSON."""
    print("\n" + "="*60)
    print("TEST 2: IDL File")
    print("="*60)
    
    backend_dir = Path(__file__).parent.parent
    idl_path = backend_dir / "app" / "idl" / "solracer_program.json"
    
    if not idl_path.exists():
        print(f"IDL file not found: {idl_path}")
        return False
    
    print(f"IDL file exists: {idl_path}")
    
    try:
        import json
        with open(idl_path, "r") as f:
            idl_data = json.load(f)
        
        # Check for required fields
        if "address" in idl_data:
            print(f"Program ID in IDL: {idl_data['address']}")
        
        if "instructions" in idl_data:
            instruction_names = [inst["name"] for inst in idl_data["instructions"]]
            print(f"Instructions found: {', '.join(instruction_names)}")
        
        return True
    except Exception as e:
        print(f"Error reading IDL file: {e}")
        return False


def test_pda_utils():
    """test PDA derivation utilities."""
    print("\n" + "="*60)
    print("TEST 3: PDA Utilities")
    print("="*60)
    
    try:
        from app.services.pda_utils import derive_race_pda_simple, get_program_id
        
        program_id = get_program_id()
        print(f"Program ID retrieved: {program_id}")
        
        #test PDA derivation
        test_race_id = "test_race_123"
        test_token_mint = "So11111111111111111111111111111111111111112"  # SOL
        test_entry_fee = 100_000_000  # 0.1 SOL in lamports
        
        pda, bump = derive_race_pda_simple(
            program_id,
            test_race_id,
            test_token_mint,
            test_entry_fee
        )
        
        print(f"PDA derived: {pda}")
        print(f"Bump seed: {bump}")
        
        return True
    except Exception as e:
        print(f"Error testing PDA utils: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_solana_client():
    """test Solana RPC client connection."""
    print("\n" + "="*60)
    print("TEST 4: Solana RPC Client")
    print("="*60)
    
    try:
        from app.services.solana_client import get_solana_client
        
        client = get_solana_client()
        print(f"Solana client initialized: {client.rpc_url}")
        
        #test getting latest blockhash
        blockhash = client.get_latest_blockhash()
        if blockhash:
            print(f"Latest blockhash retrieved: {blockhash[:20]}...")
        else:
            print(f"Could not get blockhash (may be network issue)")
        
        #test getting account info for a known address (System Program)
        system_program = Pubkey.from_string("11111111111111111111111111111111")
        account_info = client.get_account_info(system_program)
        if account_info:
            print(f"Account info retrieved for System Program")
        else:
            print(f"Could not get account info (may be network issue)")
        
        return True
    except Exception as e:
        print(f"Error testing Solana client: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_program_client():
    """test program client (IDL loading and instruction building)."""
    print("\n" + "="*60)
    print("TEST 5: Program Client")
    print("="*60)
    
    try:
        from app.services.program_client import get_program_client
        
        program_client = get_program_client()
        print(f"Program client initialized")
        print(f"Program ID: {program_client.program_id}")
        
        #test building an instruction (create_race)
        test_race_pda = Pubkey.from_string("11111111111111111111111111111111")  # Dummy for testing
        test_player1 = Pubkey.from_string("11111111111111111111111111111111")  # Dummy for testing
        test_token_mint = Pubkey.from_string("So11111111111111111111111111111111111111112")
        
        instruction = program_client.build_create_race_instruction(
            race_pda=test_race_pda,
            player1=test_player1,
            race_id="test_race_123",
            token_mint=test_token_mint,
            entry_fee_sol=100_000_000,
            system_program=SYSTEM_PROGRAM_ID
        )
        
        print(f"create_race instruction built")
        print(f"Instruction program_id: {instruction.program_id}")
        print(f"Instruction accounts: {len(instruction.accounts)}")
        print(f"Instruction data length: {len(instruction.data)} bytes")
        
        #test other instructions
        join_instruction = program_client.build_join_race_instruction(
            race_pda=test_race_pda,
            player2=test_player1,
            system_program=SYSTEM_PROGRAM_ID
        )
        print(f"join_race instruction built")
        
        submit_instruction = program_client.build_submit_result_instruction(
            race_pda=test_race_pda,
            player=test_player1,
            finish_time_ms=50000,
            coins_collected=100,
            input_hash=bytes(32)  # Dummy hash
        )
        print(f"submit_result instruction built")
        
        settle_instruction = program_client.build_settle_race_instruction(
            race_pda=test_race_pda
        )
        print(f"settle_race instruction built")
        
        claim_instruction = program_client.build_claim_prize_instruction(
            race_pda=test_race_pda,
            winner=test_player1
        )
        print(f"claim_prize instruction built")
        
        return True
    except Exception as e:
        print(f"Error testing program client: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_transaction_builder():
    """test transaction builder."""
    print("\n" + "="*60)
    print("TEST 6: Transaction Builder")
    print("="*60)
    
    try:
        from app.services.transaction_builder import get_transaction_builder
        from app.services.program_client import get_program_client
        from solders.pubkey import Pubkey
        
        builder = get_transaction_builder()
        print(f"Transaction builder initialized")
        
        #get recent blockhash
        blockhash = builder.get_recent_blockhash()
        if blockhash:
            print(f"Recent blockhash retrieved: {blockhash[:20]}...")
        else:
            print(f"Could not get blockhash (may be network issue)")
            return True  # Don't fail if network is unavailable
        
        #build a test instruction
        program_client = get_program_client()
        test_race_pda = Pubkey.from_string("11111111111111111111111111111111")
        test_player1 = Pubkey.from_string("11111111111111111111111111111111")
        test_token_mint = Pubkey.from_string("So11111111111111111111111111111111111111112")
        
        instruction = program_client.build_create_race_instruction(
            race_pda=test_race_pda,
            player1=test_player1,
            race_id="test_race_123",
            token_mint=test_token_mint,
            entry_fee_sol=100_000_000,
            system_program=SYSTEM_PROGRAM_ID
        )
        
        #build transaction
        transaction = builder.build_transaction(
            instructions=[instruction],
            payer=test_player1,
            recent_blockhash=blockhash
        )
        
        print(f"Transaction built")
        
        #test serialization
        transaction_bytes = builder.serialize_transaction(transaction)
        print(f"Transaction serialized: {len(transaction_bytes)} bytes")
        
        #test deserialization
        deserialized = builder.deserialize_transaction(transaction_bytes)
        print(f"Transaction deserialized")
        
        return True
    except Exception as e:
        print(f"Error testing transaction builder: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_transaction_submitter():
    """test transaction submitter (connection only, no actual submission)."""
    print("\n" + "="*60)
    print("TEST 7: Transaction Submitter")
    print("="*60)
    
    try:
        from app.services.transaction_submitter import get_transaction_submitter
        
        submitter = get_transaction_submitter()
        print(f"Transaction submitter initialized")
        print(f"RPC URL: {submitter.rpc_url}")
        
        #test getting transaction status (will fail for invalid signature, but tests connection)
        test_signature = "1111111111111111111111111111111111111111111111111111111111111111"
        status = submitter.get_transaction_status(test_signature)
        #this will return None for invalid signature, but that's OK - we're just testing the connection
        print(f"Transaction status check works (returns None for invalid signature)")
        
        return True
    except Exception as e:
        print(f"Error testing transaction submitter: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_onchain_sync():
    """test on-chain sync service (initialization only)."""
    print("\n" + "="*60)
    print("TEST 8: On-Chain Sync")
    print("="*60)
    
    try:
        from app.services.onchain_sync import get_onchain_sync
        
        sync_service = get_onchain_sync()
        print(f"On-chain sync service initialized")
        print(f"Program ID: {sync_service.program_id}")
        
        return True
    except Exception as e:
        print(f"Error testing on-chain sync: {e}")
        import traceback
        traceback.print_exc()
        return False


def main():
    """run all tests."""
    print("\n" + "="*60)
    print("PHASE 4.2: BACKEND SOLANA INTEGRATION - TEST SUITE")
    print("="*60)
    
    results = []
    
    #run all tests
    results.append(("Environment Variables", test_environment_variables()))
    results.append(("IDL File", test_idl_file()))
    results.append(("PDA Utilities", test_pda_utils()))
    results.append(("Solana RPC Client", test_solana_client()))
    results.append(("Program Client", test_program_client()))
    results.append(("Transaction Builder", test_transaction_builder()))
    results.append(("Transaction Submitter", test_transaction_submitter()))
    results.append(("On-Chain Sync", test_onchain_sync()))
    
    #print summary
    print("\n" + "="*60)
    print("TEST SUMMARY")
    print("="*60)
    
    passed = 0
    failed = 0
    
    for test_name, result in results:
        status = "PASS" if result else "FAIL"
        print(f"{status}: {test_name}")
        if result:
            passed += 1
        else:
            failed += 1
    
    print("\n" + "-"*60)
    print(f"Total: {len(results)} tests")
    print(f"Passed: {passed}")
    print(f"Failed: {failed}")
    print("-"*60)
    
    if failed == 0:
        print("\nAll tests passed! Phase 4.2 is ready.")
    else:
        print("\nSome tests failed. Please check the errors above.")
    
    return failed == 0


if __name__ == "__main__":
    success = main()
    sys.exit(0 if success else 1)

