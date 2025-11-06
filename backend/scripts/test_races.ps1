# Test Race Management Endpoints
# test it by running this script in powershell
# cd backend
# .\scripts\test_races.ps1

$baseUrl = "http://localhost:8000/api/v1"
$player1 = "Player1Wallet123456789"
$player2 = "Player2Wallet987654321"
$tokenMint = "So11111111111111111111111111111111111111112"
$entryFee = 0.01

Write-Host "=== Step 1: Player 1 creates/joins race ===" -ForegroundColor Cyan
$body1 = @{
    token_mint = $tokenMint
    wallet_address = $player1
    entry_fee_sol = $entryFee
} | ConvertTo-Json

$race1 = Invoke-RestMethod -Uri "$baseUrl/races/create_or_join" `
    -Method POST `
    -Body $body1 `
    -ContentType "application/json"

Write-Host "Race created: $($race1.race_id)" -ForegroundColor Green
Write-Host "Status: $($race1.status)" -ForegroundColor Yellow
$raceId = $race1.race_id

Write-Host "`n=== Step 2: Player 2 joins race ===" -ForegroundColor Cyan
$body2 = @{
    token_mint = $tokenMint
    wallet_address = $player2
    entry_fee_sol = $entryFee
} | ConvertTo-Json

$race2 = Invoke-RestMethod -Uri "$baseUrl/races/create_or_join" `
    -Method POST `
    -Body $body2 `
    -ContentType "application/json"

Write-Host "Race joined: $($race2.race_id)" -ForegroundColor Green
Write-Host "Status: $($race2.status)" -ForegroundColor Yellow

Write-Host "`n=== Step 3: Check race status ===" -ForegroundColor Cyan
$status = Invoke-RestMethod -Uri "$baseUrl/races/$raceId/status"
Write-Host "Status: $($status.status)" -ForegroundColor Yellow
Write-Host "Player 1: $($status.player1_wallet)" -ForegroundColor Yellow
Write-Host "Player 2: $($status.player2_wallet)" -ForegroundColor Yellow

Write-Host "`n=== Step 4: Player 1 submits result ===" -ForegroundColor Cyan
$inputTrace1 = @(
    @{ time = 0; action = "accelerate"; pressed = $true },
    @{ time = 1000; action = "accelerate"; pressed = $false }
)
$traceJson1 = ($inputTrace1 | ConvertTo-Json -Compress)
$hash1 = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($traceJson1))
$hashString1 = [System.BitConverter]::ToString($hash1).Replace("-", "").ToLower()

$result1 = @{
    wallet_address = $player1
    finish_time_ms = 45000
    coins_collected = 10
    input_hash = $hashString1
    input_trace = $inputTrace1
} | ConvertTo-Json -Depth 10

$submit1 = Invoke-RestMethod -Uri "$baseUrl/races/$raceId/submit_result" `
    -Method POST `
    -Body $result1 `
    -ContentType "application/json"

Write-Host "Result submitted: $($submit1.message)" -ForegroundColor Green

Write-Host "`n=== Step 5: Player 2 submits result ===" -ForegroundColor Cyan
$inputTrace2 = @(
    @{ time = 0; action = "accelerate"; pressed = $true },
    @{ time = 1200; action = "accelerate"; pressed = $false }
)
$traceJson2 = ($inputTrace2 | ConvertTo-Json -Compress)
$hash2 = [System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($traceJson2))
$hashString2 = [System.BitConverter]::ToString($hash2).Replace("-", "").ToLower()

$result2 = @{
    wallet_address = $player2
    finish_time_ms = 42000
    coins_collected = 15
    input_hash = $hashString2
    input_trace = $inputTrace2
} | ConvertTo-Json -Depth 10

$submit2 = Invoke-RestMethod -Uri "$baseUrl/races/$raceId/submit_result" `
    -Method POST `
    -Body $result2 `
    -ContentType "application/json"

Write-Host "Result submitted: $($submit2.message)" -ForegroundColor Green

Write-Host "`n=== Step 6: Check final status ===" -ForegroundColor Cyan
$finalStatus = Invoke-RestMethod -Uri "$baseUrl/races/$raceId/status"
Write-Host "Final Status: $($finalStatus.status)" -ForegroundColor Yellow
Write-Host "Winner: $($finalStatus.winner_wallet)" -ForegroundColor Green
Write-Host "Settled: $($finalStatus.is_settled)" -ForegroundColor Yellow

