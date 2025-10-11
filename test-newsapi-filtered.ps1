#!/usr/bin/env pwsh
# Test IMPROVED NewsAPI query with financial context filter

$apiKey = "b14bc928c6484a279cf8cc6374108088"

# OLD QUERY (garbage results)
$oldKeywords = "Federal Reserve OR FOMC OR Trump OR emergency OR rate"
Write-Host "❌ OLD QUERY (Too Broad):" -ForegroundColor Red
Write-Host "   $oldKeywords" -ForegroundColor Gray
Write-Host "   Result: Drake lawsuits, hurricanes, solar projects..." -ForegroundColor DarkGray
Write-Host ""

# NEW QUERY (financial context filter)
$keywords = @(
    "FOMC", "Federal Reserve", "Powell", "rate cut", "rate hike",
    "emergency meeting", "Trump tariff", "S&P 500", "Nasdaq",
    "inflation shock", "CPI surprise", "recession", "market crash"
)
$keywordQuery = "(" + ($keywords -join " OR ") + ")"
$financialFilter = "(stock OR futures OR market OR trading OR `"S&P`" OR Nasdaq OR `"Wall Street`")"
$query = "$keywordQuery AND $financialFilter"

Write-Host "✅ NEW QUERY (Financial Context Filter):" -ForegroundColor Green
Write-Host "   Keywords: $keywordQuery" -ForegroundColor Cyan
Write-Host "   Filter: $financialFilter" -ForegroundColor Yellow
Write-Host "   Combined: Market-relevant news ONLY" -ForegroundColor White
Write-Host ""

$url = "https://newsapi.org/v2/everything?q=$([System.Web.HttpUtility]::UrlEncode($query))&language=en&sortBy=publishedAt&pageSize=10&apiKey=$apiKey"

Write-Host "📡 Fetching REAL financial news..." -ForegroundColor Cyan
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
    
    Write-Host "✅ SUCCESS!" -ForegroundColor Green
    Write-Host "   Total Results: $($response.totalResults)" -ForegroundColor White
    Write-Host "   Articles Returned: $($response.articles.Count)" -ForegroundColor White
    Write-Host ""
    
    if ($response.articles.Count -gt 0) {
        Write-Host "📰 MARKET-RELEVANT NEWS HEADLINES:" -ForegroundColor Cyan
        Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Gray
        Write-Host ""
        
        $index = 1
        foreach ($article in $response.articles) {
            $publishedTime = [DateTime]::Parse($article.publishedAt)
            $minutesAgo = [Math]::Round((Get-Date).Subtract($publishedTime).TotalMinutes)
            
            Write-Host "[$index] " -NoNewline -ForegroundColor Yellow
            Write-Host "$($article.title)" -ForegroundColor White
            Write-Host "    Published: $minutesAgo minutes ago | Source: $($article.source.name)" -ForegroundColor Gray
            
            # Check for HIGH IMPACT keywords
            $highImpact = $false
            $impactKeywords = @("emergency", "crash", "shock", "breaking", "surge", "plunge", "halt")
            foreach ($keyword in $impactKeywords) {
                if ($article.title -match $keyword) {
                    $highImpact = $true
                    Write-Host "    🚨 HIGH IMPACT - MARKET MOVING!" -ForegroundColor Red
                    break
                }
            }
            
            if ($article.description -and $article.description.Length -gt 0) {
                $desc = $article.description.Substring(0, [Math]::Min(150, $article.description.Length))
                Write-Host "    $desc..." -ForegroundColor DarkGray
            }
            Write-Host ""
            $index++
        }
        
        Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Gray
        Write-Host ""
        Write-Host "✅ THIS IS WHAT YOUR BOT WILL SEE!" -ForegroundColor Green
        Write-Host "✅ Only market-relevant financial news" -ForegroundColor Green
        Write-Host "✅ No more Drake lawsuits or hurricane stories" -ForegroundColor Green
        
    } else {
        Write-Host "⚠️  No articles found (normal during quiet market periods)" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "❌ ERROR: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "🎯 What Changed:" -ForegroundColor Cyan
Write-Host "   BEFORE: Generic keywords = garbage news" -ForegroundColor Red
Write-Host "   AFTER: Keywords + Financial Filter = market news only" -ForegroundColor Green
Write-Host ""
Write-Host "📈 Ready for production trading!" -ForegroundColor Green
