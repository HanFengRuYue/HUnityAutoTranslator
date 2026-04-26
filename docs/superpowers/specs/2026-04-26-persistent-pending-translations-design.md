# HUnityAutoTranslator Persistent Pending Translations Design

Date: 2026-04-26
Status: Design approved for automatic background resume

## Goal

Captured source text must be written to the SQLite cache before provider translation starts. If the game closes while translation is still pending, the next launch should discover those untranslated rows and continue translating them in the background.

## Current Behavior

`TextPipeline` filters captured text, checks the cache, and enqueues uncached text into the in-memory `TranslationJobQueue`. `TranslationWorkerPool` writes SQLite only after a provider response succeeds. That means a crash or shutdown between capture and provider completion loses the original text unless it is captured again later.

The current SQLite row shape also requires `translated_text TEXT NOT NULL`, so the database cannot represent a durable "captured but not translated yet" state.

## Considered Approaches

### A. Persist Pending Rows And Resume Automatically

Capture writes a row with source text, provider identity, prompt policy, target language, context, timestamps, and `translated_text = NULL`. `TryGet` only returns true for rows that already have a translated value. On startup, `TranslationWorkerHost` reads pending rows for the active runtime config and enqueues them for background translation.

This is the selected approach because it preserves inspectable database rows, survives shutdown, and does not require a separate queue database.

### B. Requeue Only When The Same Text Is Captured Again

Capture still writes pending rows, but restart does not actively enqueue them. The pipeline queues them only if the same text appears again on screen.

This is simpler but fails the useful "continue old work after restart" behavior for text that does not reappear immediately.

### C. Add A Separate Persistent Queue

Keep translations table for completed results and add another SQLite table for pending jobs.

This adds more schema and synchronization work without a clear benefit because the current readable cache key already identifies pending work.

## Selected Design

Use the existing `translations` table as the durable state table.

- `translated_text` becomes nullable.
- `created_utc` records the first time this source/config row was seen.
- `updated_utc` changes when capture context is refreshed or a translation is completed.
- `TryGet` ignores rows where `translated_text IS NULL`.
- `Set` completes the row by filling `translated_text` and updating context/timestamp.
- Manual table editing/import can still complete a pending row by providing a translated value.

## Cache Contract

Extend `ITranslationCache` with:

- `RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null)` to upsert a pending row without changing an existing translation.
- `GetPendingTranslations(string targetLanguage, ProviderProfile provider, string promptPolicyVersion, int limit)` to return untranslated rows matching the current provider, model, endpoint, target language, and prompt policy.

The matching filter intentionally includes provider and prompt policy. If the user changes model, endpoint, target language, or prompt policy, old pending rows remain inspectable but are not translated under the wrong assumptions.

## Pipeline Flow

For each valid captured text:

1. Build the readable `TranslationCacheKey`.
2. Call `RecordCaptured` immediately.
3. If cache lookup is enabled and `TryGet` finds a translated value, apply the cached translation.
4. Otherwise enqueue a `TranslationJob` as before.

This makes durability independent of provider availability and worker timing.

## Startup Resume Flow

`TranslationWorkerHost` periodically checks pending rows for the current config before it checks in-memory queue idleness. It enqueues up to a bounded batch of pending rows into `TranslationJobQueue`. Queue de-duplication remains responsible for avoiding duplicate source text within pending and in-flight work.

The resume pass only runs when translation is enabled and the API key is configured. If the provider is not ready, pending rows stay in SQLite for a later pass.

## Compatibility And Schema

This project is still using a development-era cache, and prior decisions rejected legacy TSV/hash migration. If the current SQLite table has the readable schema but `translated_text` is `NOT NULL`, initialization migrates it to the nullable schema without dropping readable rows. Older hash schemas are still reset as before.

No live game cache files are deleted.

## Tests

Add focused tests for:

- SQLite records a pending row with `translated_text` NULL and `TryGet` returns false.
- Completing a pending row fills `translated_text` while preserving `created_utc`.
- Query/export can include pending rows.
- `TextPipeline` records captured rows before enqueueing.
- `TranslationWorkerHost` or an extracted resume coordinator enqueues pending rows for the active config.
