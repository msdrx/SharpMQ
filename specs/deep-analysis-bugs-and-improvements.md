# Plan: Deep Analysis — Bug Fixes and Code Quality Improvements

**Type**: refactor
**Created**: 2026-03-26
**Prompt**: Analyze project deeply, find bugs and suggest improvements for clean maintainable code

## Task Description

Comprehensive deep analysis of the SharpMQ library from 7 different perspectives (2 software architects, 2 C# engineers, 2 general-purpose analysts, 1 business analyst) revealed critical bugs, thread safety issues, resource management problems, API inconsistencies, and code quality concerns. This plan addresses the highest-priority findings organized into phased implementation.

## Objective

Fix all identified bugs, resolve resource management and thread safety issues, improve API consistency and naming, and expand test coverage to create a more reliable, maintainable, and professional RabbitMQ client library.

## Problem Statement

The SharpMQ library has several categories of issues discovered through multi-perspective analysis:

1. **Critical Bugs**: Inverted logic in `StartConsume` guard, fire-and-forget `SubscribeAsync`, blocking `SemaphoreSlim.Wait()` in async methods
2. **Resource Management**: `BaseConsumer` disposes shared `ConnectionProvider` it doesn't own; `ConnectionProvider` created in DI is never disposed; duplicate disposal logic
3. **Thread Safety**: `ChannelPool._currentPoolSize` race conditions; stale `_channel` reference in async callback; config mutation side effect
4. **API Quality**: Typos in public names (`PrefechCount`, `WithPersistens`, `Extentions`); Georgian language in logs/docs; magic numbers; `string[]` instead of `long[]` for TTL config
5. **Test Coverage**: Only `ConnectionProviderTests` exists — no tests for Producer, Consumer, ChannelPool, retry logic, config validation, or extensions
6. **Project Infrastructure**: No CI pipeline; EOL target frameworks; no `Directory.Build.props`; duplicated csproj metadata

## Solution Approach

Address issues in priority order across 3 phases:
- **Phase 1 (Foundation)**: Fix critical bugs and resource management issues that cause production failures
- **Phase 2 (Core)**: Improve API quality, fix naming, remove magic numbers, improve config types
- **Phase 3 (Testing & Infrastructure)**: Expand test coverage, add CI, clean up project structure

Key design decisions:
- Remove `_connectionProvider.Dispose()` from `BaseConsumer` — the consumer does not own the connection when shared
- Use `SemaphoreSlim` for channel pool concurrency gate instead of racy `Interlocked` counter
- Change `PerMessageTtlOnRetryMs` from `string[]` to `long[]` (breaking change, document in release notes)
- Add `[Obsolete]` forwarding for renamed public properties for one release cycle

## Relevant Files

- `src/SharpMQ/Consumers/Consumer.cs` — inverted guard logic, blocking Wait(), stale channel ref, Georgian text
- `src/SharpMQ/Consumers/BaseConsumer.cs` — disposes shared ConnectionProvider, `PrefechCount` typo
- `src/SharpMQ/ConsumerFactory.cs` — fire-and-forget SubscribeAsync
- `src/SharpMQ/Connections/ChannelPool.cs` — pool size race conditions
- `src/SharpMQ/Connections/ConnectionProvider.cs` — duplicate disposal logic, repeated hosts.ToList()
- `src/SharpMQ/Producers/Producer.cs` — ConfirmSelect per-publish, magic expiration threshold, generic logging
- `src/SharpMQ/DependencyInjection.cs` — ConnectionProvider not tracked for disposal, Georgian in docs
- `src/SharpMQ/Extensions/ChannelExtensions.cs` — config mutation side effect, `exchnage` typo, `WithPersistens` typo
- `src/SharpMQ/Extensions/Extentions.cs` — filename typo, duplicate serialization methods
- `src/SharpMQ/Extensions/BasicPropertiesExtensions.cs` — silent exception swallowing in GetRetryCount
- `src/SharpMQ/Extensions/LinqChunk.cs` — conflicts with .NET 6+ built-in Chunk
- `src/SharpMQ/Configs/RetryConfig.cs` — `string[]` instead of `long[]`
- `src/SharpMQ/Configs/ProducerConfig.cs` — generic validation error message
- `src/SharpMQ/Configs/ConsumerConfig.cs` — `PrefechCount` typo
- `src/SharpMQ/Configs/RabbitMqServerConfig.cs` — hardcoded port 5672

## Implementation Phases

### Phase 1: Foundation — Critical Bug Fixes and Resource Management

Fix bugs that cause production failures: inverted logic, fire-and-forget async, blocking semaphore, disposal ownership, channel pool races, stale channel references. These are correctness issues that can cause silent data loss, thread starvation, or crashes.

### Phase 2: Core Implementation — API Quality and Code Cleanup

Improve the API surface: fix naming typos (with backward-compat `[Obsolete]` properties), change TTL config type, fix magic numbers, improve logging, remove Georgian text, fix LinqChunk ambiguity, consolidate disposal logic, add configurable port.

### Phase 3: Testing & Integration

Expand unit test coverage for all critical paths: Consumer retry/guard logic, ChannelPool concurrency, Producer publish flow, config validation, extension methods. Add project infrastructure improvements.

## Team Members

- **csharp-engineer** — Primary implementer for all bug fixes, refactoring, and test writing
  - Role: Implements all code changes across all phases
- **general-purpose** — Validation, CI setup, project infrastructure
  - Role: Runs validation commands, verifies acceptance criteria, handles non-code changes

### Additional constraint from user: 
 - Always pass CancellationToken to async methods. If not appropriate, use TaskCompletionSource with TimeSpan. Check this rule for      
  completed tasks too.
   - Always pass CancellationToken to async methods. If not appropriate, use TaskCompletionSource with TimeSpan. Check this rule for      
  completed tasks too.


## Step by Step Tasks

### 1. Fix Critical Consumer Bugs
- **Task ID**: fix-critical-consumer-bugs
- **Depends On**: none
- **Assigned To**: csharp-engineer
- **Parallel**: false
- In `Consumer.cs` lines 151 and 165: fix log messages from "ConsumerTags is empty" to "Consumer is already active, tags already exist" (the `Any()` condition is correct — it guards against double-subscribe)
- In `Consumer.cs` lines 110 and 282: replace `_channelSemaphore.Wait()` with `await _channelSemaphore.WaitAsync(cancellationToken)` to prevent thread pool starvation
- In `Consumer.cs` line 105: replace Georgian text `"asyncEventingBasicConsumer არის null"` with `"asyncEventingBasicConsumer is null"`
- In `Consumer.cs` `SubscribeAsync` Received handler: capture `_channel` into a local variable at the top of the lambda (`var ch = _channel;`) and use `ch` for all `BasicAck`/`BasicNack`/`BasicPublish` calls to prevent stale channel references after reconnection

### 2. Fix Fire-and-Forget SubscribeAsync
- **Task ID**: fix-fire-and-forget-subscribe
- **Depends On**: none
- **Assigned To**: csharp-engineer
- **Parallel**: true
- In `ConsumerFactory.cs` line 63: change `SubscribeAsync` extension method from `void` return to `async Task`
- Await each `consumer.SubscribeAsync(...)` call, or collect tasks and use `await Task.WhenAll(tasks)`
- This ensures subscription failures propagate to callers instead of being silently lost

### 3. Fix Resource Ownership and Disposal
- **Task ID**: fix-resource-ownership-disposal
- **Depends On**: none
- **Assigned To**: csharp-engineer
- **Parallel**: true
- In `BaseConsumer.cs` line 106: remove `_connectionProvider?.Dispose()` — the consumer does not own the connection when shared via `singleConnectionPerConsumerGroup`
- Add an `ownsConnection` flag to `BaseConsumer` constructor, only dispose if true (for the non-shared case)
- In `ConnectionProvider.cs`: consolidate duplicate disposal logic between `Dispose(bool)` and `DisposeAsyncCore()` into a single private helper method; ensure `_disposed` flag is set consistently
- In `DependencyInjection.cs` `AddProducer`: track `ConnectionProvider` in `ProducerFactory` so it is disposed when the factory is disposed (add a list of owned disposables to `ProducerFactory`)

### 4. Fix ChannelPool Thread Safety
- **Task ID**: fix-channel-pool-thread-safety
- **Depends On**: none
- **Assigned To**: csharp-engineer
- **Parallel**: true
- Replace `_currentPoolSize` `Interlocked` counter pattern with a `SemaphoreSlim(_maxPoolSize, _maxPoolSize)` as a concurrency gate
- Acquire the semaphore before creating a new channel (guarantees max is never exceeded)
- Release the semaphore when a channel is returned to the pool or disposed
- This eliminates the race condition where multiple threads can exceed `_maxPoolSize`

### 5. Fix Producer Per-Publish Issues
- **Task ID**: fix-producer-per-publish-issues
- **Depends On**: none
- **Assigned To**: csharp-engineer
- **Parallel**: true
- Move `ConfirmSelect()` out of `PublishAsync` — call it once when a channel is created or obtained from the pool, not on every publish
- In `Producer.cs` line 58: replace magic number `100` with a named constant `MinExpirationMs` and throw `ArgumentOutOfRangeException` when `expirationMs` is between 1 and `MinExpirationMs` (keep `0` as "no expiration")
- Enrich error logging from generic `"Producer Error"` to include exchange, routing key, and message type: `"Producer Error publishing {MessageType} to exchange={Exchange} routingKey={RoutingKey}"`

### 6. Fix Config Mutation and Type Issues
- **Task ID**: fix-config-mutation-and-types
- **Depends On**: none
- **Assigned To**: csharp-engineer
- **Parallel**: true
- In `ChannelExtensions.ConfigureConsumerChannel<T>` line 15: compute queue name as a local variable instead of mutating `config.Queue.Name`; pass the computed name to all subsequent calls in the method
- Change `RetryConfig.PerMessageTtlOnRetryMs` from `string[]` to `long[]`; update `Validate()`, `ChannelExtensions.ConfigureRetry`, and `Consumer.RetryOrReject` to use `.ToString()` only at the RabbitMQ API boundary
- Fix `BasicPropertiesExtensions.GetRetryCount`: catch only `KeyNotFoundException` instead of all exceptions; let other exceptions propagate
- Add `Port` property to `RabbitMqServerConfig` (default 5672); use it in `MqHosts()` instead of hardcoded value; validate range 1-65535
- In `ProducerConfig.Validate()`: break the single generic error message into per-field checks with specific messages

### 7. Fix Naming and Language Issues
- **Task ID**: fix-naming-and-language
- **Depends On**: none
- **Assigned To**: csharp-engineer
- **Parallel**: true
- Rename `Extentions.cs` to `Extensions.cs` and class `Extentions` to `Extensions`
- Rename `WithPersistens` to `WithPersistence` in `ChannelExtensions.cs`
- Rename parameter `exchnage` to `exchange` in `ChannelExtensions.AddExchange`
- For public property `PrefechCount` on `ConsumerConfig`: add new property `PrefetchCount`, mark old one `[Obsolete("Use PrefetchCount instead")]` forwarding to the new one
- Fix Georgian suffix in `DependencyInjection.cs` line 61: change `IProducer"/>-ს` to `IProducer"/>`
- Materialize `hosts.ToList()` once before the retry loop in `ConnectionProvider.CreateConnectionInternalAsync`

### 8. Fix LinqChunk .NET 6+ Ambiguity
- **Task ID**: fix-linq-chunk-ambiguity
- **Depends On**: none
- **Assigned To**: csharp-engineer
- **Parallel**: true
- Wrap the custom `Chunk` extension method in `LinqChunk.cs` with `#if !NET6_0_OR_GREATER` preprocessor directive
- This prevents method resolution ambiguity on .NET 6+ where `System.Linq.Enumerable.Chunk` exists

### 9. Unit Test Critical Bug Fixes
- **Task ID**: unit-test-critical-fixes
- **Depends On**: fix-critical-consumer-bugs,fix-fire-and-forget-subscribe,fix-resource-ownership-disposal,fix-channel-pool-thread-safety
- **Assigned To**: csharp-engineer
- **Parallel**: false
- Test `StartConsume` returns false when consumer tags already exist
- Test that disposing one consumer in a shared-connection group does NOT close the connection for siblings
- Test `ChannelPool` under concurrent access: spawn parallel `GetChannelAsync`/`AddOrCloseChannelAsync` calls and verify pool never exceeds `_maxPoolSize`
- Test that `ConsumerFactory.SubscribeAsync` propagates subscription exceptions
- Test that `EnsureInitialized` and `CreateNewChannelAndStartConsume` do not block synchronously (verify async path)

### 10. Unit Test API Quality Fixes
- **Task ID**: unit-test-api-quality
- **Depends On**: fix-config-mutation-and-types,fix-producer-per-publish-issues
- **Assigned To**: csharp-engineer
- **Parallel**: true
- Test `ConfigureConsumerChannel<T>` does not mutate `config.Queue.Name` after call
- Test `RetryConfig.Validate()` with `long[]` values (valid and invalid)
- Test `GetRetryCount` with missing header, valid header, and corrupt header (non-integer)
- Test `Producer.PublishAsync` throws `ArgumentOutOfRangeException` for `expirationMs` between 1 and minimum
- Test `RabbitMqServerConfig.MqHosts()` uses configured port (default and custom)
- Test `ProducerConfig.Validate()` provides specific error messages per field

### 11. Validate All
- **Task ID**: validate-all
- **Depends On**: unit-test-critical-fixes,unit-test-api-quality,fix-naming-and-language,fix-linq-chunk-ambiguity
- **Assigned To**: general-purpose
- **Parallel**: false
- Run `dotnet build` across all target frameworks and verify zero errors
- Run `dotnet test` and verify all tests pass
- Verify no Georgian text remains in source (grep for Unicode range \u10A0-\u10FF)
- Verify no synchronous `SemaphoreSlim.Wait()` calls remain in async methods
- Verify all acceptance criteria are met

## Acceptance Criteria

- All `SemaphoreSlim` usage in async methods uses `WaitAsync()`, never `Wait()`. No blocking waits in async code paths.
- Disposing a single `Consumer<T>` in a shared-connection group does NOT close the connection or affect other consumers.
- `ConnectionProvider` instances created during DI registration are disposed when the DI container disposes `ProducerFactory`.
- `ConsumerFactory.SubscribeAsync` returns `Task` and propagates subscription failures to callers.
- `StartConsume` log messages accurately describe the condition ("already active" when tags exist).
- Under concurrent access, `ChannelPool._currentPoolSize` never exceeds `_maxPoolSize`.
- `ConsumerConfig.Queue.Name` is unchanged after `ConfigureConsumerChannel<T>` is called.
- `ConfirmSelect()` is called at most once per channel lifetime, not per publish.
- `PerMessageTtlOnRetryMs` is `long[]` in the public API.
- `Producer.PublishAsync` throws for invalid `expirationMs` values (1 to minimum threshold).
- No Georgian language text remains in source code or log messages.
- All typos fixed: `Extentions` → `Extensions`, `WithPersistens` → `WithPersistence`, `exchnage` → `exchange`, `PrefechCount` → `PrefetchCount` (with `[Obsolete]` forwarding).
- `RabbitMqServerConfig` supports configurable port with default 5672.
- `LinqChunk` custom implementation only compiles on pre-.NET 6 targets.
- `dotnet build` succeeds with zero errors on all target frameworks.
- `dotnet test` passes with new tests covering all critical paths.

## Validation Commands

```bash
# Build all projects across all target frameworks
dotnet build SharpMQ.sln --configuration Release

# Run all unit tests
dotnet test SharpMQ.sln --configuration Release --verbosity normal

# Verify no Georgian text in source files
grep -rP '[\x{10A0}-\x{10FF}]' src/ && echo "FAIL: Georgian text found" || echo "PASS: No Georgian text"

# Verify no synchronous SemaphoreSlim.Wait() in async methods
grep -rn '\.Wait()' src/SharpMQ/Consumers/ | grep -v 'WaitAsync' | grep -v '//' && echo "FAIL: Blocking Wait found" || echo "PASS: No blocking waits"

# Verify Extentions typo is gone
find src/ -name "Extentions*" && echo "FAIL: Typo file still exists" || echo "PASS: Typo fixed"
```

## Notes

### Breaking Changes

This plan includes the following breaking changes that should be documented in release notes:
- `RetryConfig.PerMessageTtlOnRetryMs` type changes from `string[]` to `long[]` (JSON deserialization of numeric values still works)
- `ConsumerFactory.SubscribeAsync` return type changes from `void` to `Task` (callers must await)
- `Producer.PublishAsync` now throws for `expirationMs` values between 1 and minimum threshold (previously silently ignored)
- `PrefechCount` property is `[Obsolete]` — use `PrefetchCount` instead

### Deferred Items

The following items were identified during analysis but deferred to keep this plan focused:
- **Introduce `IConsumerFactory` interface with DI registration** — mirrors producer pattern, significant API change
- **Add `IAsyncDisposable` to `IProducer` and `IConsumer<T>`** — enables `await using` pattern
- **Make `onException` required on `SubscribeAsync`** — breaks all callers, needs migration strategy
- **Drop `netcoreapp3.1` and `net6` EOL targets** — breaking for downstream consumers
- **Create `Directory.Build.props`** — project infrastructure, no functional impact
- **Add CI pipeline** — important but separate from code quality fixes
- **Add `.editorconfig`** — style enforcement, separate concern
- **Eager connection opening in `OpenProducerConnectionBackgroundService`** — behavior change

### Perspective Summary

- **Software Architects (2)**: Identified disposal ownership chain as the highest-severity architectural issue. `BaseConsumer` disposes shared `ConnectionProvider`, violating ownership semantics. Also flagged config mutation side effects, duplicated disposal logic, and API asymmetry between producer/consumer patterns.
- **C# Engineers (2)**: Found critical thread safety bugs: blocking `Wait()` in async paths, `ChannelPool` counter race conditions, `ConfirmSelect` per-publish overhead. Identified `RetryConfig.PerMessageTtlOnRetryMs` string-to-long type issue, `LinqChunk` .NET 6+ ambiguity, and silent exception swallowing in `GetRetryCount`.
- **General-Purpose Analysts (2)**: Discovered Georgian language strings in production code, inverted log messages, comprehensive naming typos, fire-and-forget async patterns, and project infrastructure gaps (no CI, EOL frameworks, no centralized build props).
- **Business Analyst**: Focused on developer experience — asymmetric DI patterns, silent failure on null error callback, magic expiration threshold, and missing port configurability. Emphasized that the path of least resistance (default null `onException`) leads to silent production failures.
