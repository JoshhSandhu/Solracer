$ErrorActionPreference = "Continue"
$base = "https://localhost:8001/api/v1"

# Bypass self-signed cert check (PowerShell 5 compatible)
add-type @"
  using System.Net; using System.Security.Cryptography.X509Certificates;
  public class TrustAll : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int err) { return true; }
  }
"@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAll

# 1. Create race
Write-Host "`n[1] Creating race..."
$race = Invoke-RestMethod -Method Post -Uri "$base/races/create" `
  -ContentType "application/json" `
  -Body '{"token_mint":"So11111111111111111111111111111111111111112","wallet_address":"WalletPlayerOne","entry_fee_sol":0.01,"is_private":false}'
$raceId = $race.race_id
Write-Host "race_id: $raceId"

# 2. Player 2 joins
Write-Host "`n[2] Player 2 joining..."
Invoke-RestMethod -Method Post -Uri "$base/races/$raceId/join" `
  -ContentType "application/json" `
  -Body '{"wallet_address":"WalletPlayerTwo"}' | ConvertTo-Json

# 3. Ghost update from player 1
Write-Host "`n[3] Ghost update from WalletPlayerOne..."
$body3 = @{ race_id=$raceId; wallet_address="WalletPlayerOne"; x=100.5; y=200.3; speed=12.0; checkpoint_index=2; seq=1 } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$base/ghost/update" `
  -ContentType "application/json" -Body $body3 | ConvertTo-Json

# 4. GET ghost state for race
Write-Host "`n[4] GET ghost/$raceId..."
Invoke-RestMethod -Uri "$base/ghost/$raceId" | ConvertTo-Json -Depth 4

# 5. Spoof attempt - expect 403
Write-Host "`n[5] Spoof attempt from FakeWallet (expect 403)..."
$body5 = @{ race_id=$raceId; wallet_address="FakeWallet"; x=0; y=0; speed=0; checkpoint_index=0; seq=1 } | ConvertTo-Json
try {
  Invoke-RestMethod -Method Post -Uri "$base/ghost/update" `
    -ContentType "application/json" -Body $body5 | ConvertTo-Json
} catch {
  Write-Host "Got HTTP $($_.Exception.Response.StatusCode) - as expected"
}
