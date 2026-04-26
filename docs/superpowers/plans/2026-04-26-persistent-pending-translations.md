# Persistent Pending Translations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist captured source text before translation and automatically resume untranslated cache rows after game restart.

**Architecture:** Extend the cache contract so `translations` can represent pending rows with nullable `translated_text`. `TextPipeline` records every valid capture before queueing, and `TranslationWorkerHost` feeds pending rows for the active runtime config back into the existing in-memory queue.

**Tech Stack:** C# `netstandard2.1`; Microsoft.Data.Sqlite; Newtonsoft.Json; xUnit; FluentAssertions.

---

## File Structure

- Modify `src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs`: add pending-row methods.
- Modify `src/HUnityAutoTranslator.Core/Caching/TranslationCacheEntry.cs`: allow `TranslatedText` to be null.
- Modify `src/HUnityAutoTranslator.Core/Caching/SqliteTranslationCache.cs`: nullable schema, pending upsert, pending query.
- Modify `src/HUnityAutoTranslator.Core/Caching/MemoryTranslationCache.cs`: implement pending behavior for tests.
- Modify `src/HUnityAutoTranslator.Core/Caching/DiskTranslationCache.cs`: implement no-loss interface behavior for compatibility.
- Modify `src/HUnityAutoTranslator.Core/Pipeline/TextPipeline.cs`: record captured source text before cache lookup/queue.
- Modify `src/HUnityAutoTranslator.Plugin/TranslationWorkerHost.cs`: periodically enqueue pending SQLite rows for the current config.
- Modify `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`: cover pending rows and schema migration.
- Modify `tests/HUnityAutoTranslator.Core.Tests/Pipeline/TextPipelineTests.cs`: cover capture-before-queue.
- Modify `tests/HUnityAutoTranslator.Core.Tests/Queueing/WorkerPoolTests.cs`: update test doubles for the extended cache contract.

## Task 1: Cache Pending Row Contract

**Files:**
- Modify: `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/TranslationCacheEntry.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/SqliteTranslationCache.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/MemoryTranslationCache.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/DiskTranslationCache.cs`

- [ ] **Step 1: Write failing SQLite pending-row tests**

Add tests asserting `RecordCaptured` creates a row whose `translated_text` is NULL, `TryGet` returns false, `GetPendingTranslations` returns it, and `Set` later completes it while preserving `created_utc`.

- [ ] **Step 2: Run the focused cache tests**

Run: `dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~TranslationCacheTests"`

Expected before implementation: compile failure because the cache interface does not expose pending-row methods.

- [ ] **Step 3: Implement the contract**

Add `RecordCaptured` and `GetPendingTranslations` to the cache interface and all implementations. Change SQLite `translated_text` to nullable and make `TryGet` filter out pending rows.

- [ ] **Step 4: Run the focused cache tests again**

Run the same command and require exit code 0.

## Task 2: Pipeline Capture Persistence

**Files:**
- Modify: `tests/HUnityAutoTranslator.Core.Tests/Pipeline/TextPipelineTests.cs`
- Modify: `src/HUnityAutoTranslator.Core/Pipeline/TextPipeline.cs`
- Modify: `tests/HUnityAutoTranslator.Core.Tests/Queueing/WorkerPoolTests.cs`

- [ ] **Step 1: Write failing pipeline test**

Add a recording cache test proving `TextPipeline.Process` calls `RecordCaptured` for a valid uncached capture before the queued job is processed.

- [ ] **Step 2: Run the focused pipeline tests**

Run: `dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~TextPipelineTests"`

Expected before implementation: failure because no captured row is recorded.

- [ ] **Step 3: Implement capture persistence**

Call `RecordCaptured` after creating the cache key and before `TryGet`/enqueue.

- [ ] **Step 4: Run the focused pipeline tests again**

Run the same command and require exit code 0.

## Task 3: Startup Pending Resume

**Files:**
- Modify: `src/HUnityAutoTranslator.Plugin/TranslationWorkerHost.cs`
- Test indirectly through cache and queue behavior where possible.

- [ ] **Step 1: Add resume enqueue logic**

Before the worker loop sleeps on an empty queue, query pending rows for the current config and enqueue bounded jobs with stored context.

- [ ] **Step 2: Run queue and worker tests**

Run: `dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~WorkerPoolTests|FullyQualifiedName~TranslationQueueTests"`

Expected after implementation: exit code 0.

## Task 4: Full Verification

**Files:**
- All touched files.

- [ ] **Step 1: Run all tests**

Run: `dotnet test HUnityAutoTranslator.sln`

- [ ] **Step 2: Build package**

Run: `powershell -ExecutionPolicy Bypass -File build/package.ps1 -Configuration Release`

- [ ] **Step 3: Inspect git diff**

Run: `git diff -- src tests docs`

Verify the diff only includes the pending translation persistence feature and the new Superpowers docs.
