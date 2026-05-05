using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Queueing;

namespace HUnityAutoTranslator.Core.Tests.Pipeline;

public sealed class TextPipelineTests
{
    [Fact]
    public void Process_returns_cached_translation_without_queueing()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Start Game", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "开始游戏");
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "Start Game", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        decision.TranslatedText.Should().Be("开始游戏");
        queue.PendingCount.Should().Be(0);
    }

    [Fact]
    public void Process_returns_cached_translation_without_recording_pending_capture()
    {
        var cache = new CountingTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Start Game", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "开始游戏");
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "Start Game", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        cache.RecordCapturedCallCount.Should().Be(0);
        queue.PendingCount.Should().Be(0);
    }

    [Fact]
    public void Process_records_pending_capture_only_when_new_job_is_enqueued()
    {
        var cache = new CountingTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var pipeline = new TextPipeline(cache, queue, config);
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Options", "UnityEngine.UI.Text");

        pipeline.Process(new CapturedText("ui-1", "Options", isVisible: true, context));
        pipeline.Process(new CapturedText("ui-2", "Options", isVisible: true, context));

        cache.RecordCapturedCallCount.Should().Be(1);
        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Process_ignores_source_text_that_is_already_simplified_chinese()
    {
        var cache = new CountingTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault();
        var pipeline = new TextPipeline(cache, queue, config, metrics);

        var decision = pipeline.Process(new CapturedText("ui-1", "\u6e38\u620f\u8bbe\u7f6e", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.Ignored);
        cache.RecordCapturedCallCount.Should().Be(0);
        cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10).Should().BeEmpty();
        queue.PendingCount.Should().Be(0);
        metrics.Snapshot().CapturedTextCount.Should().Be(0);
    }

    [Theory]
    [InlineData("v81")]
    [InlineData("HUnityAutoTranslator.Plugin.dll")]
    public void Process_ignores_preservable_identifiers(string sourceText)
    {
        var cache = new CountingTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault();
        var pipeline = new TextPipeline(cache, queue, config, metrics);

        var decision = pipeline.Process(new CapturedText("ui-1", sourceText, isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.Ignored);
        cache.RecordCapturedCallCount.Should().Be(0);
        queue.PendingCount.Should().Be(0);
        metrics.Snapshot().CapturedTextCount.Should().Be(0);
    }

    [Fact]
    public void Process_uses_custom_prompt_template_hash_in_cache_key()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var customized = config with
        {
            PromptTemplates = new PromptTemplateConfig(SystemPrompt: "Custom {TargetLanguage}")
        };
        cache.Set(
            TranslationCacheKey.Create("Start Game", customized.TargetLanguage, customized.Provider, TextPipeline.PromptPolicyVersion),
            "旧缓存");
        var pipeline = new TextPipeline(cache, queue, customized);

        var decision = pipeline.Process(new CapturedText("ui-1", "Start Game", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        TextPipeline.GetPromptPolicyVersion(customized).Should().StartWith(TextPipeline.PromptPolicyVersion + "-");
        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Process_skips_cached_translation_when_required_glossary_term_is_missing()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Find Freddy", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "找到佛莱迪");
        glossary.UpsertManual(GlossaryTerm.CreateManual("Freddy", "弗雷迪", config.TargetLanguage, null));
        var pipeline = new TextPipeline(cache, queue, config, metrics: null, glossary: glossary);

        var decision = pipeline.Process(new CapturedText("ui-1", "Find Freddy", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Process_skips_cached_translation_when_quality_rules_fail()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Ultra", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        var context = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI");
        cache.Set(key, "\u8d85", context);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "Ultra", isVisible: true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.PendingCount.Should().Be(1);
        cache.TryGet(key, context, out _).Should().BeFalse();
        var pending = cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10);
        pending.Should().ContainSingle();
        pending[0].TranslatedText.Should().BeNull();
        pending[0].ProviderKind.Should().BeEmpty();
    }

    [Fact]
    public void Process_marks_cached_source_language_translation_pending_and_requeues()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var context = new TranslationCacheContext("Top Vertical", "Canvas/Settings/Graphic Level/Text", "TMPro.TMP_Text");
        var key = TranslationCacheKey.Create("\u753b\u8cea\u30ec\u30d9\u30eb", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "\u753b\u8cea\u30ec\u30d9\u30eb", context);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "\u753b\u8cea\u30ec\u30d9\u30eb", isVisible: true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.PendingCount.Should().Be(1);
        cache.TryGet(key, context, out _).Should().BeFalse();
        var pending = cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10);
        pending.Should().ContainSingle(row =>
            row.SourceText == "\u753b\u8cea\u30ec\u30d9\u30eb" &&
            row.TranslatedText == null &&
            row.SceneName == context.SceneName &&
            row.ComponentHierarchy == context.ComponentHierarchy);
    }

    [Fact]
    public void Process_skips_cached_translation_when_generated_outer_symbols_are_cached()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Settings", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        var context = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Main/Title", "TMPro.TextMeshProUGUI");
        cache.Set(key, "\u0022\u8bbe\u7f6e\u0022", context);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "Settings", isVisible: true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.PendingCount.Should().Be(1);
        cache.TryGet(key, context, out _).Should().BeFalse();
        var pending = cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10);
        pending.Should().ContainSingle();
        pending[0].TranslatedText.Should().BeNull();
        pending[0].ProviderKind.Should().BeEmpty();
    }

    [Fact]
    public void Process_skips_cached_translation_when_ui_marker_symbol_is_missing()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("> Join a crew", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        var context = new TranslationCacheContext("Lobby", "Canvas/Menu/JoinCrew", "UnityEngine.UI.Text");
        cache.Set(key, "\u52a0\u5165\u961f\u4f0d", context);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "> Join a crew", isVisible: true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.PendingCount.Should().Be(1);
        cache.TryGet(key, context, out _).Should().BeFalse();
        var pending = cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10);
        pending.Should().ContainSingle();
        pending[0].TranslatedText.Should().BeNull();
        pending[0].ProviderKind.Should().BeEmpty();
    }

    [Fact]
    public void Process_marks_cached_structured_json_translation_pending_even_when_quality_rules_are_disabled()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault() with
        {
            TranslationQuality = TranslationQualityConfig.Default() with { Enabled = false }
        };
        var key = TranslationCacheKey.Create("Settings", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        var context = new TranslationCacheContext("Main Menu", "Canvas/Menu/Settings", "TMPro.TextMeshProUGUI");
        cache.Set(key, """{"text_index":0,"text":"\u8bbe\u7f6e"}""", context);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "Settings", isVisible: true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.PendingCount.Should().Be(1);
        cache.TryGet(key, context, out _).Should().BeFalse();
        var pending = cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10);
        pending.Should().ContainSingle();
        pending[0].TranslatedText.Should().BeNull();
        pending[0].ProviderKind.Should().BeEmpty();
    }

    [Fact]
    public void Process_uses_cached_translation_when_quality_rules_are_disabled()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault() with
        {
            TranslationQuality = TranslationQualityConfig.Default() with { Enabled = false }
        };
        var key = TranslationCacheKey.Create("Ultra", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        var context = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI");
        cache.Set(key, "\u8d85", context);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "Ultra", isVisible: true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        decision.TranslatedText.Should().Be("\u8d85");
        queue.PendingCount.Should().Be(0);
        cache.TryGet(key, context, out var cached).Should().BeTrue();
        cached.Should().Be("\u8d85");
    }

    [Fact]
    public void Process_ignores_already_simplified_chinese_source_with_short_technical_token()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("\u753b\u8d28 FPS", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        var context = new TranslationCacheContext("Settings", "Canvas/Settings/Graphics/FpsCounter", "UnityEngine.UI.Text");
        cache.Set(key, "\u753b\u8d28 FPS", context);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "\u753b\u8d28 FPS", isVisible: true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.Ignored);
        decision.TranslatedText.Should().BeNull();
        queue.PendingCount.Should().Be(0);
    }

    [Fact]
    public void Process_uses_capture_context_when_reading_cached_translation()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Back", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        var menuContext = new TranslationCacheContext("MainMenu", "Canvas/Menu/Back", "UnityEngine.UI.Text");
        var hudContext = new TranslationCacheContext("Gameplay", "Canvas/Hud/Back", "UnityEngine.UI.Text");
        cache.Set(key, "返回菜单", menuContext);
        cache.Set(key, "返回", hudContext);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("menu-back", "Back", isVisible: true, menuContext));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        decision.TranslatedText.Should().Be("返回菜单");
        queue.PendingCount.Should().Be(0);
    }

    [Fact]
    public void Process_reuses_same_source_translation_when_best_candidate_is_unambiguous()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("On", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "\u5f00\u542f", new TranslationCacheContext("Main Menu", "Menu/Screen Panel/Reticle/Text", "UnityEngine.UI.Text"));
        cache.Set(key, "\u5df2\u542f\u7528", new TranslationCacheContext("testRoom_1", "User/PAUSE/Settings Menu/Screen Panel/Subtitles/Text", "UnityEngine.UI.Text"));
        cache.Set(key, "\u5df2\u542f\u7528", new TranslationCacheContext("testRoom_1", "User/PAUSE/Settings Menu/Screen Panel/V-Sync/Text", "UnityEngine.UI.Text"));
        var captureContext = new TranslationCacheContext("testRoom_1", "User/PAUSE/Settings Menu/Screen Panel/Reticle/Text", "UnityEngine.UI.Text");
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("reticle-on", "On", isVisible: true, captureContext));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        decision.TranslatedText.Should().Be("\u5df2\u542f\u7528");
        queue.PendingCount.Should().Be(0);
        cache.TryGet(key, captureContext, out var materialized).Should().BeTrue();
        materialized.Should().Be("\u5df2\u542f\u7528");
    }

    [Fact]
    public void Process_reuses_same_setting_group_switch_translation_and_rejects_short_variant()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault() with
        {
            GameTitle = "\u6c60\u888b\u30bb\u30af\u30b5\u30ed\u30a4\u30c9\u5973\u5b66\u5712"
        };
        var key = TranslationCacheKey.Create("On", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "\u5f00", new TranslationCacheContext("Top Vertical", "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Auto Message Lady/Switch/Label On", "TMPro.TMP_Text"));
        cache.Set(key, "\u5f00\u542f", new TranslationCacheContext("Top Vertical", "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Emotion Effect Enable/Switch/Label On", "TMPro.TMP_Text"));
        var captureContext = new TranslationCacheContext("Top Vertical", "Canvas - Main Menu/Main Content/Settings (1)/Content/Panel Content/Panels/General/Content/List/Layout Group/Particle Effect Enable/Switch/Label On", "TMPro.TMP_Text");
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("particle-on", "On", isVisible: true, captureContext));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        decision.TranslatedText.Should().Be("\u5f00\u542f");
        queue.PendingCount.Should().Be(0);
        cache.TryGet(key, captureContext, out var materialized).Should().BeTrue();
        materialized.Should().Be("\u5f00\u542f");
    }

    [Fact]
    public void ResolveCachedTranslationOnly_uses_exact_cache_without_queueing_or_recording_pending()
    {
        var cache = new CountingTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var context = new TranslationCacheContext(
            "PlayerSceneNight",
            "GUIRoot/GameSettingGUI/CuteSettingCanvasGUI/Window/InfoRoot/InfoAreaControl/Viewport/Content/Slider_Mouse Speed X Cute/LabelArea/TitleText",
            "UnityEngine.UI.Text");
        var key = TranslationCacheKey.Create("Camera Speed X", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "\u955c\u5934\u901f\u5ea6 X", context);
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.ResolveCachedTranslationOnly(new CapturedText("target", "Camera Speed X", true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        decision.TranslatedText.Should().Be("\u955c\u5934\u901f\u5ea6 X");
        cache.RecordCapturedCallCount.Should().Be(0);
        queue.PendingCount.Should().Be(0);
        cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10).Should().BeEmpty();
    }

    [Fact]
    public void ResolveCachedTranslationOnly_materializes_unambiguous_reused_translation_without_queueing()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Video", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "\u89c6\u9891", new TranslationCacheContext(
            "GameStartScene",
            "SettingCanvas/SettingArea/MenuArea/List/Button_Video/Text",
            "UnityEngine.UI.Text"));
        var captureContext = new TranslationCacheContext(
            "PlayerSceneNight",
            "GUIRoot/GameSettingGUI/CuteSettingCanvasGUI/Tabs/Video/Label",
            "UnityEngine.UI.Text");
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.ResolveCachedTranslationOnly(new CapturedText("target", "Video", true, captureContext));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        decision.TranslatedText.Should().Be("\u89c6\u9891");
        queue.PendingCount.Should().Be(0);
        cache.TryGet(key, captureContext, out var materialized).Should().BeTrue();
        materialized.Should().Be("\u89c6\u9891");
    }

    [Fact]
    public void ResolveCachedTranslationOnly_does_not_queue_when_no_cache_match_exists()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var context = new TranslationCacheContext("PlayerSceneNight", "Canvas/Settings/Missing", "UnityEngine.UI.Text");
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.ResolveCachedTranslationOnly(new CapturedText("target", "Camera Speed Z", true, context));

        decision.Kind.Should().Be(PipelineDecisionKind.Ignored);
        decision.TranslatedText.Should().BeNull();
        queue.PendingCount.Should().Be(0);
        cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10).Should().BeEmpty();
    }

    [Fact]
    public void Process_does_not_reuse_same_source_translation_when_candidates_conflict_without_context_match()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Back", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "\u8fd4\u56de\u83dc\u5355", new TranslationCacheContext("Main Menu", "Canvas/Menu/Back", "UnityEngine.UI.Text"));
        cache.Set(key, "\u540e\u9000", new TranslationCacheContext("Gameplay", "Canvas/Hud/Back", "UnityEngine.UI.Text"));
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText(
            "credits-back",
            "Back",
            isVisible: true,
            new TranslationCacheContext("Credits", "Canvas/Credits/Back", "UnityEngine.UI.Text")));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Process_queues_uncached_visible_text_with_high_priority()
    {
        var queue = new TranslationJobQueue();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, RuntimeConfig.CreateDefault());

        var decision = pipeline.Process(new CapturedText("ui-2", "Options", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.TryDequeueBatch(1, 100, out var batch).Should().BeTrue();
        batch[0].Priority.Should().Be(TranslationPriority.VisibleUi);
    }

    [Fact]
    public void Process_queues_capture_context_for_cache_write()
    {
        var queue = new TranslationJobQueue();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, RuntimeConfig.CreateDefault());
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Options", "UnityEngine.UI.Text");

        pipeline.Process(new CapturedText("ui-2", "Options", isVisible: true, context));

        queue.TryDequeueBatch(1, 100, out var batch).Should().BeTrue();
        batch[0].Context.Should().Be(context);
    }

    [Fact]
    public void Process_preserves_publish_result_flag_for_imgui_cache_only_jobs()
    {
        var queue = new TranslationJobQueue();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, RuntimeConfig.CreateDefault());
        var context = new TranslationCacheContext("title_01", null, "IMGUI");

        pipeline.Process(new CapturedText("imgui:FreeShop", "FreeShop", isVisible: true, context, publishResult: false));

        queue.TryDequeueBatch(1, 100, out var batch).Should().BeTrue();
        batch[0].PublishResult.Should().BeFalse();
    }

    [Fact]
    public void Process_records_valid_capture_in_cache_before_queueing()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var pipeline = new TextPipeline(cache, queue, config);
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Options", "UnityEngine.UI.Text");

        pipeline.Process(new CapturedText("ui-2", "Options", isVisible: true, context));

        var pending = cache.GetPendingTranslations(config.TargetLanguage, TextPipeline.PromptPolicyVersion, limit: 10);
        pending.Should().ContainSingle();
        pending[0].SourceText.Should().Be("Options");
        pending[0].TranslatedText.Should().BeNull();
        pending[0].ProviderKind.Should().BeEmpty();
        pending[0].ProviderModel.Should().BeEmpty();
        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Process_queues_original_source_text_for_writeback_matching()
    {
        var queue = new TranslationJobQueue();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, RuntimeConfig.CreateDefault());
        var source = "  Start   Game  ";

        pipeline.Process(new CapturedText("ui-2", source, isVisible: true));

        queue.TryDequeueBatch(1, 100, out var batch).Should().BeTrue();
        batch[0].SourceText.Should().Be(source);
    }

    [Fact]
    public void Process_reads_latest_runtime_config_from_provider()
    {
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, () => config);

        config = config with { Enabled = false };
        var decision = pipeline.Process(new CapturedText("ui-3", "Options", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.Ignored);
        queue.PendingCount.Should().Be(0);
    }

    [Fact]
    public void Process_records_captured_and_queued_metrics()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var pipeline = new TextPipeline(cache, queue, RuntimeConfig.CreateDefault(), metrics);

        pipeline.Process(new CapturedText("target", "Start Game", true, TranslationCacheContext.Empty));

        var snapshot = metrics.Snapshot();
        snapshot.CapturedTextCount.Should().Be(1);
        snapshot.QueuedTextCount.Should().Be(1);
    }

    [Fact]
    public void Process_records_metrics_once_for_repeated_pending_source_text()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var pipeline = new TextPipeline(cache, queue, RuntimeConfig.CreateDefault(), metrics);

        pipeline.Process(new CapturedText("target-1", "Start Game", true, TranslationCacheContext.Empty));
        pipeline.Process(new CapturedText("target-2", "Start Game", true, TranslationCacheContext.Empty));

        var snapshot = metrics.Snapshot();
        snapshot.CapturedTextCount.Should().Be(1);
        snapshot.QueuedTextCount.Should().Be(1);
        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Process_does_not_count_cache_hit_as_completed_translation()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Start Game", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "Start translated");
        var pipeline = new TextPipeline(cache, queue, config, metrics);

        pipeline.Process(new CapturedText("target", "Start Game", true, TranslationCacheContext.Empty));

        var snapshot = metrics.Snapshot();
        snapshot.CapturedTextCount.Should().Be(1);
        snapshot.CompletedTranslationCount.Should().Be(0);
        snapshot.RecentTranslations.Should().BeEmpty();
    }

    [Fact]
    public void Process_does_not_record_capture_metrics_for_ignored_text()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var pipeline = new TextPipeline(cache, queue, RuntimeConfig.CreateDefault(), metrics);

        pipeline.Process(new CapturedText("target", "12345", true, TranslationCacheContext.Empty));

        metrics.Snapshot().CapturedTextCount.Should().Be(0);
    }

    private sealed class CountingTranslationCache : ITranslationCache
    {
        private readonly MemoryTranslationCache _inner = new();

        public int RecordCapturedCallCount { get; private set; }

        public bool TryGet(TranslationCacheKey key, TranslationCacheContext? context, out string translatedText)
        {
            return _inner.TryGet(key, context, out translatedText);
        }

        public bool TryGetReplacementFont(TranslationCacheKey key, TranslationCacheContext context, out string replacementFont)
        {
            return _inner.TryGetReplacementFont(key, context, out replacementFont);
        }

        public IReadOnlyList<TranslationCacheEntry> GetCompletedTranslationsBySource(TranslationCacheKey key, int limit)
        {
            return _inner.GetCompletedTranslationsBySource(key, limit);
        }

        public void RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null)
        {
            RecordCapturedCallCount++;
            _inner.RecordCaptured(key, context);
        }

        public void Set(TranslationCacheKey key, string translatedText, TranslationCacheContext? context = null)
        {
            _inner.Set(key, translatedText, context);
        }

        public IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(string targetLanguage, string promptPolicyVersion, int limit)
        {
            return _inner.GetPendingTranslations(targetLanguage, promptPolicyVersion, limit);
        }

        public IReadOnlyList<TranslationContextExample> GetTranslationContextExamples(
            string currentSourceText,
            string targetLanguage,
            TranslationCacheContext? context,
            int maxExamples,
            int maxCharacters)
        {
            return _inner.GetTranslationContextExamples(currentSourceText, targetLanguage, context, maxExamples, maxCharacters);
        }

        public int Count => _inner.Count;

        public TranslationCachePage Query(TranslationCacheQuery query)
        {
            return _inner.Query(query);
        }

        public TranslationCacheFilterOptionPage GetFilterOptions(TranslationCacheFilterOptionsQuery query)
        {
            return _inner.GetFilterOptions(query);
        }

        public void Update(TranslationCacheEntry entry)
        {
            _inner.Update(entry);
        }

        public void Delete(TranslationCacheEntry entry)
        {
            _inner.Delete(entry);
        }

        public string Export(string format)
        {
            return _inner.Export(format);
        }

        public TranslationCacheImportResult Import(string content, string format)
        {
            return _inner.Import(content, format);
        }
    }
}
