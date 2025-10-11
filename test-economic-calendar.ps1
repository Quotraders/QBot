# Test Economic Calendar Loading
Write-Host "[*] Testing Economic Calendar Loading..." -ForegroundColor Cyan
Write-Host ""

$calendarPath = "datasets\economic_calendar\calendar.json"

if (Test-Path $calendarPath) {
    Write-Host "[+] Found calendar file: $calendarPath" -ForegroundColor Green
    
    $calendar = Get-Content $calendarPath -Raw | ConvertFrom-Json
    $totalEvents = $calendar.Count
    
    Write-Host "[+] Total Events: $totalEvents" -ForegroundColor Green
    Write-Host ""
    
    # Show upcoming events
    $today = Get-Date
    $upcomingEvents = $calendar | Where-Object { 
        $eventDate = [DateTime]::Parse($_.date)
        $eventDate -ge $today
    } | Select-Object -First 5
    
    if ($upcomingEvents.Count -gt 0) {
        Write-Host "[*] UPCOMING HIGH-IMPACT EVENTS:" -ForegroundColor Cyan
        Write-Host ""
        
        foreach ($event in $upcomingEvents) {
            Write-Host "    [!!] $($event.date) $($event.time) ET" -ForegroundColor Yellow
            Write-Host "         $($event.event)" -ForegroundColor White
            Write-Host ""
        }
    }
    
    Write-Host "[+] Economic calendar is ready!" -ForegroundColor Green
    Write-Host "    Bot will restrict trading 30 minutes before these events" -ForegroundColor Gray
    Write-Host ""
    
} else {
    Write-Host "[!] ERROR: Calendar file not found at $calendarPath" -ForegroundColor Red
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "[+] Economic Calendar Test Complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
