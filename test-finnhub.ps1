# Test FinnHub API Connection
# Quick script to verify FinnHub economic calendar API works

Write-Host "[*] Testing FinnHub API Connection..." -ForegroundColor Cyan
Write-Host ""

# Get API key from environment
$apiKey = $env:ECONOMIC_API_KEY
if ([string]::IsNullOrEmpty($apiKey)) {
    # Try loading from .env file
    if (Test-Path ".env") {
        Write-Host "[+] Loading API key from .env file..." -ForegroundColor Yellow
        $envContent = Get-Content ".env" -Raw
        if ($envContent -match 'ECONOMIC_API_KEY=([^\r\n]+)') {
            $apiKey = $matches[1]
            Write-Host "[+] Found API key in .env file" -ForegroundColor Green
        }
    }
}

if ([string]::IsNullOrEmpty($apiKey)) {
    Write-Host "[!] ERROR: ECONOMIC_API_KEY not found!" -ForegroundColor Red
    Write-Host "    Set it with: `$env:ECONOMIC_API_KEY='your_key'" -ForegroundColor Yellow
    Write-Host "    Or add to .env file: ECONOMIC_API_KEY=your_key" -ForegroundColor Yellow
    exit 1
}

Write-Host "[+] API Key: $($apiKey.Substring(0, 10))..." -ForegroundColor Green
Write-Host ""

# Build FinnHub URL
$fromDate = (Get-Date).ToString("yyyy-MM-dd")
$toDate = (Get-Date).AddDays(30).ToString("yyyy-MM-dd")
$url = "https://finnhub.io/api/v1/calendar/economic?from=$fromDate" + "&to=$toDate" + "&token=$apiKey"

Write-Host "[*] Date Range: $fromDate to $toDate" -ForegroundColor Cyan
Write-Host "[*] Endpoint: https://finnhub.io/api/v1/calendar/economic" -ForegroundColor Cyan
Write-Host ""

try {
    Write-Host "[*] Sending request to FinnHub..." -ForegroundColor Yellow
    
    # Make HTTP request
    $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
    
    Write-Host "[+] SUCCESS! FinnHub API responded" -ForegroundColor Green
    Write-Host ""
    
    # Parse response
    if ($response.economicCalendar) {
        $events = $response.economicCalendar
        $totalEvents = $events.Count
        
        Write-Host "[*] RESULTS:" -ForegroundColor Cyan
        Write-Host "    Total Events: $totalEvents" -ForegroundColor White
        Write-Host ""
        
        # Count by impact
        $highImpact = ($events | Where-Object { $_.impact -ge 2 }).Count
        $mediumImpact = ($events | Where-Object { $_.impact -eq 1 }).Count
        $lowImpact = ($events | Where-Object { $_.impact -eq 0 }).Count
        
        Write-Host "    Impact Breakdown:" -ForegroundColor Cyan
        Write-Host "    - High Impact (2 or more): $highImpact events" -ForegroundColor Red
        Write-Host "    - Medium Impact (1): $mediumImpact events" -ForegroundColor Yellow
        Write-Host "    - Low Impact (0): $lowImpact events" -ForegroundColor Gray
        Write-Host ""
        
        # Show first 5 high/medium impact events
        $importantEvents = $events | Where-Object { $_.impact -ge 1 } | Select-Object -First 5
        
        if ($importantEvents.Count -gt 0) {
            Write-Host "[*] UPCOMING HIGH/MEDIUM IMPACT EVENTS:" -ForegroundColor Cyan
            Write-Host ""
            
            foreach ($event in $importantEvents) {
                $impactLabel = switch ($event.impact) {
                    3 { "[!!!] HOLIDAY" }
                    2 { "[!!] HIGH" }
                    1 { "[!] MEDIUM" }
                    default { "[-] LOW" }
                }
                
                $eventTime = [DateTime]::Parse($event.time)
                Write-Host "    $impactLabel - $($eventTime.ToString('yyyy-MM-dd HH:mm'))" -ForegroundColor White
                Write-Host "    $($event.country): $($event.event)" -ForegroundColor Gray
                Write-Host ""
            }
        }
        
        Write-Host "[+] FinnHub API is working correctly!" -ForegroundColor Green
        Write-Host "    Your bot will receive this data on startup." -ForegroundColor Green
        Write-Host ""
        
    } else {
        Write-Host "[!] WARNING: FinnHub responded but data format unexpected" -ForegroundColor Yellow
        Write-Host "    Response: $($response | ConvertTo-Json -Depth 2)" -ForegroundColor Gray
    }
    
} catch {
    Write-Host "[!] ERROR: Failed to connect to FinnHub API" -ForegroundColor Red
    Write-Host ""
    Write-Host "    Error Message: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    
    if ($_.Exception.Message -match "401") {
        Write-Host "    [!] 401 Unauthorized - Invalid API Key" -ForegroundColor Yellow
        Write-Host "    Solution: Check your FinnHub API key is correct" -ForegroundColor Yellow
    }
    elseif ($_.Exception.Message -match "403") {
        Write-Host "    [!] 403 Forbidden - API key may be expired" -ForegroundColor Yellow
        Write-Host "    Solution: Generate a new API key from finnhub.io" -ForegroundColor Yellow
    }
    elseif ($_.Exception.Message -match "429") {
        Write-Host "    [!] 429 Rate Limit - Too many requests" -ForegroundColor Yellow
        Write-Host "    Solution: Wait a moment and try again" -ForegroundColor Yellow
    }
    else {
        Write-Host "    Possible causes:" -ForegroundColor Yellow
        Write-Host "    - Internet connection issue" -ForegroundColor Yellow
        Write-Host "    - FinnHub API temporarily down" -ForegroundColor Yellow
        Write-Host "    - Firewall blocking HTTPS requests" -ForegroundColor Yellow
    }
    
    Write-Host ""
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "[+] FinnHub API Test Complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
