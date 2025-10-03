# Analyzer Fix Guidebook (v1.0)

## How to use
Before fixing any compiler/analyzer error:
- Fix **code** — never weaken analyzers or add suppressions.
- Prefer **immutable-by-default** domain models (collections exposed read-only).
- Move thresholds/fees/timeouts/windows to **strongly-typed config** with validation (no magic numbers).

---

## Priority order
1) Correctness & invariants: S109, CA1062, CA1031/S2139, CS0165, CS0200, CS0201, CS0818, CS0103/CS0117/CS1501/CS1929  
2) API & encapsulation: CA2227, CA1051/S1104, CA1002, CA1034, CA2234, CA1003  
3) Logging & diagnosability: CA1848, CA2254, S1481, S1541  
4) Globalization & string ops: CA1305/1307/1304/1308/1310/1311  
5) Async/Dispose/Resource safety & perf: CA2000, CA1063, CA1854, CA1869, S2681, S3358, S1066, S2589, S1905  
6) Style/micro-perf: CA1822/S2325, CA1707, S6608/S6667/S6580, CA1860, CA1826, S4487, CA1819

---

## "Always" fixes
- **CA2227 / CS0200 (collection mutability):** Domain objects expose collections read-only. Use backing + `Replace*` and update call sites.
  ```csharp
  private readonly List<Trade> _trades = new();
  public IReadOnlyList<Trade> Trades => _trades;
  public void ReplaceTrades(IEnumerable<Trade> items)
  { _trades.Clear(); if (items != null) _trades.AddRange(items); }
  ```

- **CA1062 (public arg null-guards):**
  ```csharp
  public Result Execute(OrderSpec spec)
  { if (spec is null) throw new ArgumentNullException(nameof(spec)); /*...*/ }
  ```

- **CA1848 / CA2254 (logging):** Structured logging only (templates or LoggerMessage). No interpolation.
  ```csharp
  _log.LogInformation("Filled {Symbol} at {Price}", fill.Symbol, fill.Price);
  ```

- **CA2000 / CA1063 (dispose):** Own it → dispose it. using/await using; implement proper Dispose/DisposeAsync.

- **CS0165 / CS0818:** Initialize locals; don't use var without initializer.

- **CS0103 / CS0117 / CS1501 / CS1929:** Use real APIs (correct using/type/member/signature/receiver). No dummy members/overloads.

## "Depends" fixes (decision rules)

- **S109 magic numbers:** Usually move to config or named constants.
  Allowed inline: obvious sentinel values (-1,0,1) and explicit time helpers (TimeSpan.FromSeconds(5)).

- **Globalization:** Protocols/tickers/logs → InvariantCulture + StringComparison.Ordinal. UI text → CurrentCulture. Normalize keys with ToUpperInvariant().

- **S1541/S3358/S1066/S2589:** Refactor for clarity. Exception: proven hot-paths—document and micro-benchmark.

- **CA1822 / S2325:** Make static if no instance state. Exceptions: interface/override or imminent instance use (document).

- **CA1002:** Expose IReadOnlyList<T>/IEnumerable<T> not List<T>. Exception: internal APIs where controlled mutation is intended.

## Recipes by rule (most common)

- **S109:** Move numeric thresholds/fees/leverage/timeouts to strongly-typed options bound from config and validated on startup.

- **CA1062:** Null-guard all public/entry points and external integration boundaries.

- **CA1031 / S2139:** Catch specific exceptions; log context; rethrow or return controlled failure. No empty catch.

- **CA2227 / CS0200:** Collections stay read-only; use Replace* methods and fix call sites. DTOs can use init + map to domain.

- **CA1848 / CA2254:** Use ILogger templates or LoggerMessage source-gen; never interpolate strings in log calls.

- **CA1305/1307/1304/1308/1310/1311:** Always pass CultureInfo and StringComparison.

- **CA2000 / CA1063:** Wrap disposables; implement disposal pattern; don't create/dispose HttpClient per call (use factory/singleton).

- **CA1854:** Prefer TryGetValue to avoid double lookup.

- **CA1869:** Reuse a single JsonSerializerOptions instance.

- **S2681 / S3358 / S1066 / S2589 / S1905:** Braces always; un-nest ternaries; merge simple if's; remove constant conditions and redundant casts.

- **CA1822 / S2325:** Mark helper methods static.

- **CA1707:** Public identifiers in PascalCase (tests can use [DisplayName] for readability).

## Examples

### DTO vs domain for CS0200/CA2227

```csharp
public sealed record StrategyDto { public required string Name { get; init; }
  public required List<string> Symbols { get; init; } = new(); }

public sealed class Strategy {
  private readonly List<string> _symbols = new();
  public IReadOnlyList<string> Symbols => _symbols;
  public void ReplaceSymbols(IEnumerable<string> items)
  { _symbols.Clear(); _symbols.AddRange(items ?? Array.Empty<string>()); }
  public static Strategy FromDto(StrategyDto dto) { var s=new Strategy(); s.ReplaceSymbols(dto.Symbols); return s; }
}
```

### Globalization

```csharp
var s = amount.ToString(CultureInfo.InvariantCulture);
if (symbol.StartsWith("ES", StringComparison.Ordinal)) { /*...*/ }
var key = symbol.ToUpperInvariant();
```

### Resilience boundary (CA1031)

```csharp
while (!ct.IsCancellationRequested) {
  try { await _orchestrator.PollAsync(ct); }
  catch (ExchangeThrottleException ex) { _log.LogWarning(ex,"Throttle: backing off"); await Task.Delay(TimeSpan.FromSeconds(2), ct); }
  catch (Exception ex) { _log.LogError(ex,"Fatal loop error; stopping"); await _risk.KillSwitchAsync("fatal-loop", ct); break; }
}
```

## Pre-commit self-checks (run locally)
```bash
# New public setters (bad—esp. collections)
rg -n 'public\s+[^\{]+\{\s*get;\s*set;\s*\}\s*$'
rg -n '(List|Dictionary|I(ReadOnly)?(List|Dictionary))<.+>\s*\{\s*get;\s*set;'

# Magic numbers (skip config/markdown)
rg -n --glob '!**/*.json' --glob '!**/*.md' '[^A-Za-z0-9_](\d{2,}|0?\.\d{2,})[^A-Za-z0-9_]'

# Swallowed exceptions
rg -n 'catch\s*\(\s*Exception[^\)]*\)\s*\{(\s*//.*)?\s*\}'
```

## PR author checklist (must tick all)

- dotnet build -warnaserror green; dotnet test green
- No posture edits; no suppressions
- No new public setters on collections/domain state
- Sonar Quality Gate PASS (Reliability A); duplication within policy
- Attach tools/analyzers/current.sarif + short "fixed rules → how" summary

---

## Hedge-Fund-Grade Guardrails

### Determinism, Time, and Clocks

**Monotonic time in hot paths:** Use `Stopwatch.GetTimestamp()` for latency/SLA measurements. Reserve `DateTimeOffset.UtcNow`/NodaTime for wall-clock/session logic. Hold trading if clock skew > 300ms vs NTP.

**Bar boundary discipline:** All features/gates evaluate on bar close using a single scheduler. Forbid ad-hoc timers. DST handled via exchange calendar—no local time math.

### Types That Make Invalid States Unrepresentable

**Money/Price/Ticks value objects:** Replace raw `decimal` with readonly struct wrappers:
- `Price(decimal, TickSize)`
- `Money(decimal, Currency)` 
- `Ticks(int)`

Centralize tick rounding (`AlignForLimit`/`Stop`) inside these types. Ban passing naked `decimal` to order APIs.

**Banned types in price paths:** Disallow `double`/`float` in any method that touches orders, PnL, or SL/TP (use Roslyn banned-symbols analyzer).

### Async + Cancellation Contracts

**Every I/O method takes CancellationToken:** No timeouts via `.Wait()`/`Task.Delay(x)` without CT.

**TCS hygiene:** Use `new TaskCompletionSource(..., TaskCreationOptions.RunContinuationsAsynchronously)` only.

**IAsyncDisposable everywhere:** Sockets, SignalR, file streams, ONNX sessions → `await using`.

### State Durability & Crash Consistency

**Atomic writes only:** temp → `File.Replace`; fsync (`FileStream.Flush(true)`) for ledgers, snapshots, and state/*.

**Warm restart contract:** On boot, load last-good snapshots for zones, patterns, positions. If missing → fail closed and alert.

### Execution & Risk Circuit Breakers

**Latency/queue SLAs:** Per order choose offset to target P(fill ≥ 0.8 in 3s). Cancel/replace if ETA breaches SLA.

**Portfolio guards:** Correlation/β cap across ES/NQ. Daily loss lock enforced in policy and adapter. Halt after N rejects/min to prevent thrash.

**Kill switch semantics:** File + remote + API. Idempotent. Flatten net position, stop new orders, and freeze config snapshot.

### Data Quality & Lineage

**Freshness & schema sentries:** If a feature feed is older than 2× cadence or schema hash changes → drop `knowledge_weight` to 0 and alert.

**Lineage stamp on decisions:** Persist `{dataset_sha, model_sha, config.snapshot_id, code_version}` per trade for audit/replay.

### Model Governance

**Per-regime canary & rollback:** Promote only when canary beats control by pre-registered metrics. Auto-rollback on drift/latency spikes.

**Reproducible inference:** Pin ONNX Runtime version. Verify model sha256 on load. Refuse to run on mismatch.

### Observability Budgets (Avoid Cardinality Explosions)

**Metric keys limited & bounded:** Whitelists for tags (symbol, strategy, reason). Drop free-form strings. Alerts on high-cardinality.

**Explainability stamp:** Tiny JSON blob per decision (top 5 features + gates) stored with trade. Size-capped.

### Security & Secrets

**No secrets in logs:** Guard `ToString()` of configs. Redact token-like strings.

**Device-bound live trading:** Enforce "local device only" check before any live order path. VPN/VPS → hard fail + audit.

### Testing & Chaos

**Golden parity tests:** Same snapshot → identical decisions in live vs backtest.

**Chaos drills:** Inject slow network (p50→p95×3), dropped ticks, and reconnects. Assert the policy holds not trades.

**Tick-grid tests:** Assert every submitted price is on grid for ES/MES/NQ/MNQ.

### CI/CD Rails

**Banned APIs file:** Block `.Result`/`.Wait()`/`GetAwaiter().GetResult()` and `double` in price paths.

**Perf budget test:** Hot decision path ≤ X ms at p95 (set X). Test fails on regression.

---

## Solution-Level "Fail the Build" Rails

### Directory.Build.props - Analyzer Enforcement

Ensure these properties are set at solution level to enforce quality gates:

```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.VisualStudio.Threading.Analyzers" Version="17.*" PrivateAssets="all" />
    <PackageReference Include="Meziantou.Analyzer" Version="2.*" PrivateAssets="all" />
    <PackageReference Include="AsyncFixer" Version="1.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### Roslyn Banned API File

Create `analyzers/BannedSymbols.txt` to prevent re-introducing bugs:

```
T:System.Threading.Thread.Sleep; Use Task.Delay with CancellationToken
M:System.Threading.Tasks.Task.get_Result; Do not block on async
M:System.Threading.Tasks.Task.Wait; Do not block on async
M:System.Runtime.CompilerServices.TaskAwaiter.GetResult; Do not block on async
M:System.DateTime.get_Now; Use DateTimeOffset.UtcNow or Stopwatch for latency
T:System.Double; Disallowed in price/PnL paths (use decimal);#price-path
```

Reference it in `Directory.Build.props`:

```xml
<ItemGroup>
  <AdditionalFiles Include="analyzers/BannedSymbols.txt" />
</ItemGroup>
```

### .editorconfig - Culture, Naming, Decimal Literals

Add these rules to enforce best practices:

```ini
[*.cs]
# Culture-safe operations
dotnet_style_prefer_invariant_globalization = true:suggestion
dotnet_diagnostic.CA1305.severity = error
dotnet_diagnostic.CA1307.severity = error

# Code style
dotnet_style_prefer_simplified_boolean_expressions = true:warning

# Require 'm' suffix for financial literals (decimal enforcement)
dotnet_diagnostic.IDE0004.severity = error

# Async hygiene
dotnet_diagnostic.VSTHRD002.severity = error   # Avoid blocking waits
dotnet_diagnostic.VSTHRD103.severity = error   # Call async methods correctly
dotnet_diagnostic.VSTHRD200.severity = error   # Use Async naming convention

# Threading analyzers
dotnet_diagnostic.MA0045.severity = error      # Use ConfigureAwait(false)
dotnet_diagnostic.AsyncFixer01.severity = error # Unnecessary async/await
dotnet_diagnostic.AsyncFixer02.severity = error # Long-running operations on thread pool
```

These rails ensure:
- **Build-time prevention** of common bugs
- **Cultural safety** in all string operations  
- **Async hygiene** enforcement at compile time
- **Type safety** for monetary values (decimal over double)
- **Banned APIs** cannot be used without explicit suppression with justification

---