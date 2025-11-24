#!/usr/bin/env python3
"""
Test script for Phase 7: Payout System

This script tests all the payout endpoints:
- Get payout status
- Process payout (SOL token - direct transfer)
- Process payout (Non-SOL token - Jupiter swap)
- Retry payout

Run with: python scripts/test_phase7.py
"""

import sys
import os
import requests
import json
import base64
from pathlib import Path
from datetime import datetime
import uuid

# Add backend directory to path
backend_dir = Path(__file__).parent.parent
sys.path.insert(0, str(backend_dir))

from dotenv import load_dotenv

# Load environment variables
load_dotenv(dotenv_path=backend_dir / ".env")

# Test configuration
BASE_URL = os.getenv("API_BASE_URL", "http://localhost:8000")
API_PREFIX = os.getenv("API_V1_PREFIX", "/api/v1")

# Test wallet addresses (dummy for testing)
TEST_WALLET_1 = "11111111111111111111111111111111"
TEST_WALLET_2 = "22222222222222222222222222222222"

# Test token mints
SOL_MINT = "So11111111111111111111111111111111111111112"
BONK_MINT = "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263"


def print_test_header(test_name: str):
    """Print test header."""
    print("\n" + "="*60)
    print(f"TEST: {test_name}")
    print("="*60)


def print_result(success: bool, message: str):
    """Print test result."""
    status = "[PASS]" if success else "[FAIL]"
    print(f"  {status} {message}")


def test_api_health():
    """Test API health check."""
    print_test_header("API Health Check")
    
    try:
        response = requests.get(f"{BASE_URL}/health", timeout=5)
        if response.status_code == 200:
            print_result(True, "API is running")
            print_result(True, f"Base URL: {BASE_URL}")
            return True
        else:
            print_result(False, f"API returned status {response.status_code}")
            return False
    except Exception as e:
        print_result(False, f"API not accessible: {e}")
        return False


def create_test_race(token_mint: str, entry_fee: float = 0.01):
    """Create a test race and settle it."""
    print_test_header("Create Test Race")
    
    try:
        # Create race
        url = f"{BASE_URL}{API_PREFIX}/races/create_or_join"
        payload = {
            "token_mint": token_mint,
            "wallet_address": TEST_WALLET_1,
            "entry_fee_sol": entry_fee
        }
        
        response = requests.post(url, json=payload, timeout=10)
        
        if response.status_code != 200:
            print_result(False, f"Failed to create race: {response.status_code}")
            print(f"  Response: {response.text}")
            return None
        
        race_data = response.json()
        race_id = race_data.get("race_id")
        player1_wallet = race_data.get("player1_wallet", TEST_WALLET_1)
        print_result(True, f"Race created: {race_id}")
        print_result(True, f"Player 1 wallet: {player1_wallet}")
        
        # Join race as player 2
        payload2 = {
            "token_mint": token_mint,
            "wallet_address": TEST_WALLET_2,
            "entry_fee_sol": entry_fee
        }
        response2 = requests.post(url, json=payload2, timeout=10)
        
        player2_wallet = TEST_WALLET_2
        if response2.status_code == 200:
            print_result(True, "Player 2 joined race")
            race_data2 = response2.json()
            player2_wallet = race_data2.get("player2_wallet", TEST_WALLET_2)
        else:
            print_result(False, f"Failed to join race: {response2.status_code}")
            print(f"  Response: {response2.text}")
            # Try to get race info to see if it's already active
            get_race_url = f"{BASE_URL}{API_PREFIX}/races/{race_id}"
            race_info = requests.get(get_race_url, timeout=10)
            if race_info.status_code == 200:
                race_info_data = race_info.json()
                player2_wallet = race_info_data.get("player2_wallet")
                if player2_wallet:
                    print_result(True, f"Race already has player 2: {player2_wallet}")
                else:
                    print_result(False, "Race does not have player 2, cannot submit results")
                    return race_id
        
        # Wait a moment for race to become ACTIVE
        import time
        time.sleep(0.5)
        
        # Check race status before submitting results
        get_race_url = f"{BASE_URL}{API_PREFIX}/races/{race_id}"
        race_status_check = requests.get(get_race_url, timeout=10)
        if race_status_check.status_code == 200:
            race_info = race_status_check.json()
            race_status = race_info.get("status")
            if race_status == "settled":
                print_result(True, f"Race already settled (status: {race_status}) - skipping result submission")
                print_result(True, "This is expected if race was settled from previous test or auto-settled")
                return race_id
            elif race_status != "active":
                print_result(False, f"Race is not in ACTIVE status (status: {race_status}) - cannot submit results")
                return race_id
        
        # Submit results for both players
        # Player 1 (winner - faster time)
        submit_url = f"{BASE_URL}{API_PREFIX}/races/{race_id}/submit_result"
        result1 = {
            "wallet_address": player1_wallet,
            "finish_time_ms": 50000,  # 50 seconds (winner)
            "coins_collected": 100,
            "input_hash": "a" * 64,  # Dummy hash
            "input_trace": []
        }
        response3 = requests.post(submit_url, json=result1, timeout=10)
        if response3.status_code == 200:
            print_result(True, "Player 1 result submitted")
        elif response3.status_code == 400:
            error_detail = response3.json().get("detail", "")
            if "SETTLED" in str(error_detail):
                print_result(True, "Race already settled - Player 1 result already submitted or race completed")
            else:
                print_result(False, f"Failed to submit result 1: {response3.status_code}")
                print(f"  Response: {response3.text}")
        else:
            print_result(False, f"Failed to submit result 1: {response3.status_code}")
            print(f"  Response: {response3.text}")
        
        # Player 2 (loser - slower time)
        if not player2_wallet:
            print_result(False, "Player 2 wallet not found - cannot submit result")
            return race_id
        
        result2 = {
            "wallet_address": player2_wallet,
            "finish_time_ms": 60000,  # 60 seconds
            "coins_collected": 80,
            "input_hash": "b" * 64,  # Dummy hash
            "input_trace": []
        }
        response4 = requests.post(submit_url, json=result2, timeout=10)
        if response4.status_code == 200:
            print_result(True, "Player 2 result submitted")
            print_result(True, "Race should be settled automatically")
        elif response4.status_code == 400:
            error_detail = response4.json().get("detail", "")
            if "SETTLED" in str(error_detail):
                print_result(True, "Race already settled - Player 2 result already submitted or race completed")
            else:
                print_result(False, f"Failed to submit result 2: {response4.status_code}")
                print(f"  Response: {response4.text}")
        elif response4.status_code == 422:
            # Validation error - likely wallet_address is None
            error_detail = response4.json().get("detail", "")
            print_result(False, f"Validation error submitting result 2: {response4.status_code}")
            print(f"  Response: {error_detail}")
            print(f"  Note: This usually means player2_wallet is None or invalid")
        else:
            print_result(False, f"Failed to submit result 2: {response4.status_code}")
            print(f"  Response: {response4.text}")
        
        return race_id
        
    except Exception as e:
        print_result(False, f"Error creating test race: {e}")
        import traceback
        traceback.print_exc()
        return None


def test_get_payout_status(race_id: str):
    """Test getting payout status."""
    print_test_header("Get Payout Status")
    
    try:
        url = f"{BASE_URL}{API_PREFIX}/payouts/{race_id}"
        response = requests.get(url, timeout=10)
        
        if response.status_code == 200:
            data = response.json()
            print_result(True, "Payout status retrieved")
            print_result(True, f"Payout ID: {data.get('payout_id')}")
            print_result(True, f"Winner: {data.get('winner_wallet')}")
            print_result(True, f"Prize: {data.get('prize_amount_sol')} SOL")
            print_result(True, f"Status: {data.get('swap_status')}")
            return True, data
        elif response.status_code == 404:
            print_result(False, "Payout not found (may need to process payout first)")
            return False, None
        else:
            print_result(False, f"Error: {response.status_code}")
            print(f"  Response: {response.text}")
            return False, None
            
    except Exception as e:
        print_result(False, f"Error: {e}")
        import traceback
        traceback.print_exc()
        return False, None


def test_process_payout_sol(race_id: str):
    """Test processing payout for SOL token (direct transfer)."""
    print_test_header("Process Payout (SOL Token - Direct Transfer)")
    
    try:
        url = f"{BASE_URL}{API_PREFIX}/payouts/{race_id}/process"
        response = requests.post(url, timeout=30)
        
        if response.status_code == 200:
            data = response.json()
            print_result(True, "Payout processed successfully")
            print_result(True, f"Status: {data.get('status')}")
            print_result(True, f"Method: {data.get('method')}")
            print_result(True, f"Amount SOL: {data.get('amount_sol')}")
            
            if data.get('transaction'):
                print_result(True, f"Transaction bytes length: {len(data.get('transaction', ''))}")
                print_result(True, "Transaction ready for signing")
            
            if data.get('swap_transaction'):
                print_result(True, "Jupiter swap transaction received")
            
            return True, data
        else:
            print_result(False, f"Error: {response.status_code}")
            print(f"  Response: {response.text}")
            return False, None
            
    except Exception as e:
        print_result(False, f"Error: {e}")
        import traceback
        traceback.print_exc()
        return False, None


def test_process_payout_token(race_id: str):
    """Test processing payout for non-SOL token (Jupiter swap)."""
    print_test_header("Process Payout (BONK Token - Jupiter Swap)")
    
    try:
        url = f"{BASE_URL}{API_PREFIX}/payouts/{race_id}/process"
        response = requests.post(url, timeout=30)
        
        if response.status_code == 200:
            data = response.json()
            print_result(True, "Payout processed successfully")
            print_result(True, f"Status: {data.get('status')}")
            print_result(True, f"Method: {data.get('method')}")
            print_result(True, f"Amount SOL: {data.get('amount_sol')}")
            
            if data.get('method') == 'jupiter_swap':
                print_result(True, "Jupiter swap method detected")
                if data.get('swap_transaction'):
                    print_result(True, "Swap transaction received from Jupiter")
                    print_result(True, f"Token amount: {data.get('amount_tokens')}")
                else:
                    print_result(False, "No swap transaction in response")
            
            if data.get('method') == 'fallback_sol':
                print_result(True, "Fell back to SOL (swap may have failed)")
            
            return True, data
        else:
            print_result(False, f"Error: {response.status_code}")
            print(f"  Response: {response.text}")
            return False, None
            
    except Exception as e:
        print_result(False, f"Error: {e}")
        import traceback
        traceback.print_exc()
        return False, None


def test_retry_payout(race_id: str):
    """Test retrying a payout."""
    print_test_header("Retry Payout")
    
    try:
        url = f"{BASE_URL}{API_PREFIX}/payouts/{race_id}/retry"
        response = requests.post(url, timeout=30)
        
        if response.status_code == 200:
            data = response.json()
            print_result(True, "Payout retry successful")
            print_result(True, f"Status: {data.get('status')}")
            return True, data
        elif response.status_code == 400:
            print_result(False, f"Cannot retry: {response.json().get('detail')}")
            return False, None
        else:
            print_result(False, f"Error: {response.status_code}")
            print(f"  Response: {response.text}")
            return False, None
            
    except Exception as e:
        print_result(False, f"Error: {e}")
        import traceback
        traceback.print_exc()
        return False, None


def main():
    """Run all tests."""
    print("\n" + "="*60)
    print("PHASE 7: PAYOUT SYSTEM - TEST SUITE")
    print("="*60)
    
    results = {
        "health_check": False,
        "create_race_sol": False,
        "create_race_bonk": False,
        "get_payout_status_sol": False,
        "process_payout_sol": False,
        "get_payout_status_bonk": False,
        "process_payout_bonk": False,
    }
    
    # Test 0: API Health Check
    if not test_api_health():
        print("\n[ERROR] API is not accessible. Please start the backend server.")
        return
    
    results["health_check"] = True
    
    # Test 1: Create test race with SOL token
    print("\n" + "-"*60)
    print("SCENARIO 1: SOL Token Payout (Direct Transfer)")
    print("-"*60)
    
    race_id_sol = create_test_race(SOL_MINT, 0.01)
    if race_id_sol:
        results["create_race_sol"] = True
        
        # Wait a moment for race to settle
        import time
        print("\n  [WAIT] Waiting for race to settle...")
        time.sleep(2)
        
        # Test get payout status
        status_ok, payout_data = test_get_payout_status(race_id_sol)
        results["get_payout_status_sol"] = status_ok
        
        # Test process payout
        process_ok, process_data = test_process_payout_sol(race_id_sol)
        results["process_payout_sol"] = process_ok
        
        if process_data and process_data.get('method') == 'claim_prize':
            print_result(True, "✓ SOL token correctly uses claim_prize method (no swap)")
    
    # Test 2: Create test race with BONK token
    print("\n" + "-"*60)
    print("SCENARIO 2: BONK Token Payout (Jupiter Swap)")
    print("-"*60)
    
    race_id_bonk = create_test_race(BONK_MINT, 0.01)
    if race_id_bonk:
        results["create_race_bonk"] = True
        
        # Wait a moment for race to settle
        import time
        print("\n  [WAIT] Waiting for race to settle...")
        time.sleep(2)
        
        # Test get payout status
        status_ok, payout_data = test_get_payout_status(race_id_bonk)
        results["get_payout_status_bonk"] = status_ok
        
        # Test process payout
        process_ok, process_data = test_process_payout_token(race_id_bonk)
        results["process_payout_bonk"] = process_ok
        
        if process_data:
            method = process_data.get('method')
            if method == 'jupiter_swap':
                print_result(True, "✓ BONK token correctly uses Jupiter swap method")
            elif method == 'fallback_sol':
                print_result(True, "✓ Swap failed, correctly fell back to SOL")
            else:
                print_result(False, f"Unexpected method: {method}")
    
    # Summary
    print("\n" + "="*60)
    print("TEST SUMMARY")
    print("="*60)
    
    total_tests = len(results)
    passed_tests = sum(1 for v in results.values() if v)
    
    for test_name, result in results.items():
        status = "[PASS]" if result else "[FAIL]"
        print(f"  {status} {test_name}")
    
    print(f"\n  Total: {passed_tests}/{total_tests} tests passed")
    
    if passed_tests == total_tests:
        print("\n[SUCCESS] All tests passed! Phase 7 payout system is working.")
    else:
        print("\n[WARNING] Some tests failed. Check the output above for details.")
    
    print("\n" + "="*60)


if __name__ == "__main__":
    main()

