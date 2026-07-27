# Deterministic BCL rule inventory

<!-- Generated from Clockwork.Instrumentation.Rules.BuiltIn.RuleInventoryDocument.Render().
     Do not edit by hand; a test verifies this file matches the shipped rule set. -->

Rule set id: `clockwork.bcl.deterministic`  
Version: `1.0.0`  
Shim assembly: `Clockwork.Runtime`

This is the exact, exhaustive surface the built-in rule set redirects. Every other API is **not** rewritten. Outside an active simulation each shim runs the real BCL API unchanged; under an active simulation with no registered runtime environment the shim fails explicitly rather than fall back to real time or randomness.

## Clock family

Policy: **Controlled**. Wall-clock, offset-clock, monotonic timestamp, and tick-counter reads dispatch to the node's simulated clock. Local-time APIs honour the configured simulation time zone; tick counters wrap with correct `int`/`long` semantics.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.datetime.now` | `System.DateTime::get_Now()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetNow()` | Controlled |
| `clockwork.bcl.datetime.utcnow` | `System.DateTime::get_UtcNow()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetUtcNow()` | Controlled |
| `clockwork.bcl.datetime.today` | `System.DateTime::get_Today()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetToday()` | Controlled |
| `clockwork.bcl.datetimeoffset.now` | `System.DateTimeOffset::get_Now()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetOffsetNow()` | Controlled |
| `clockwork.bcl.datetimeoffset.utcnow` | `System.DateTimeOffset::get_UtcNow()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetOffsetUtcNow()` | Controlled |
| `clockwork.bcl.stopwatch.gettimestamp` | `System.Diagnostics.Stopwatch::GetTimestamp()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetTimestamp()` | Controlled |
| `clockwork.bcl.stopwatch.getelapsedtime` | `System.Diagnostics.Stopwatch::GetElapsedTime(System.Int64)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetElapsedTime(System.Int64)` | Controlled |
| `clockwork.bcl.environment.tickcount` | `System.Environment::get_TickCount()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetTickCount()` | Controlled |
| `clockwork.bcl.environment.tickcount64` | `System.Environment::get_TickCount64()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetTickCount64()` | Controlled |

## Identity family

Policy: **Controlled**. GUIDs draw deterministic bytes while preserving RFC 4122 variant and version. `CreateVersion7` encodes the simulated UTC millisecond timestamp in the first 48 bits; repeated calls at the same simulated instant share that timestamp (no monotonicity guarantee beyond the BCL contract).

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.guid.newguid` | `System.Guid::NewGuid()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicGuid::NewGuid()` | Controlled |
| `clockwork.bcl.guid.createversion7` | `System.Guid::CreateVersion7()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicGuid::CreateVersion7()` | Controlled |
| `clockwork.bcl.guid.createversion7.timestamp` | `System.Guid::CreateVersion7(System.DateTimeOffset)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicGuid::CreateVersion7(System.DateTimeOffset)` | Controlled |

## Random family

Policy: **Controlled**. `Random.Shared` and unseeded `new Random()` become per-node deterministic streams isolated from the scheduler/network/Buggify seed domains; explicitly seeded `new Random(int)` preserves the caller's seed exactly, matching normal BCL behaviour.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.random.shared` | `System.Random::get_Shared()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicRandom::GetShared()` | Controlled |
| `clockwork.bcl.random.ctor.unseeded` | `new System.Random()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicRandom::CreateUnseeded()` | Controlled |
| `clockwork.bcl.random.ctor.seeded` | `new System.Random(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicRandom::CreateSeeded(System.Int32)` | Controlled |

## Crypto family

Policy: **Rejected**. Static entropy APIs are redirected to a policy shim. The default under simulation is a precise rejected-call diagnostic; a test-only opt-in can substitute deterministic-insecure bytes. Production security semantics are never changed.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.rng.create` | `System.Security.Cryptography.RandomNumberGenerator::Create()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::Create()` | Rejected |
| `clockwork.bcl.rng.create.named` | `System.Security.Cryptography.RandomNumberGenerator::Create(System.String)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::Create(System.String)` | Rejected |
| `clockwork.bcl.rng.fill` | `System.Security.Cryptography.RandomNumberGenerator::Fill(System.Span`1<System.Byte>)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::Fill(System.Span`1<System.Byte>)` | Rejected |
| `clockwork.bcl.rng.getbytes.count` | `System.Security.Cryptography.RandomNumberGenerator::GetBytes(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetBytes(System.Int32)` | Rejected |
| `clockwork.bcl.rng.getint32.exclusive` | `System.Security.Cryptography.RandomNumberGenerator::GetInt32(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetInt32(System.Int32)` | Rejected |
| `clockwork.bcl.rng.getint32.range` | `System.Security.Cryptography.RandomNumberGenerator::GetInt32(System.Int32,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetInt32(System.Int32,System.Int32)` | Rejected |
| `clockwork.bcl.rng.gethexstring.span` | `System.Security.Cryptography.RandomNumberGenerator::GetHexString(System.Span`1<System.Char>,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetHexString(System.Span`1<System.Char>,System.Boolean)` | Rejected |
| `clockwork.bcl.rng.gethexstring.length` | `System.Security.Cryptography.RandomNumberGenerator::GetHexString(System.Int32,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetHexString(System.Int32,System.Boolean)` | Rejected |
| `clockwork.bcl.rng.getstring` | `System.Security.Cryptography.RandomNumberGenerator::GetString(System.ReadOnlySpan`1<System.Char>,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetString(System.ReadOnlySpan`1<System.Char>,System.Int32)` | Rejected |

## Documented holes (not rewritten in this rule set)

These nondeterministic or entropy-drawing surfaces are intentionally **not** covered by
Phase 5 and remain real BCL calls even under simulation:

- `Stopwatch` instance APIs (`Start`/`Stop`/`Restart`/`Elapsed`/`ElapsedMilliseconds`/`ElapsedTicks`) and the `GetElapsedTime(long, long)` overload.
- Generic cryptographic helpers `RandomNumberGenerator.GetItems<T>` and `Shuffle<T>`, and any `GetString`/`GetHexString` overloads beyond those listed above.
- `DateTime`/`DateTimeOffset` parsing/formatting and any culture-, timezone-, or kind-conversion helpers other than the `Now`/`UtcNow`/`Today` clocks above.
- Everything outside time/identity/random: task/thread/synchronization primitives, timers, collections, Buggify, hosting, and network/HTTP. These are out of scope for Phase 5.

Determinism is claimed **only** for the exact rules tabulated above.
