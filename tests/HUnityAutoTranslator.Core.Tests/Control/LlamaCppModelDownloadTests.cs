using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class LlamaCppModelDownloadTests
{
    [Fact]
    public void BuiltInPresets_include_translation_models_with_verified_metadata()
    {
        var presets = LlamaCppModelDownloadPresets.All;

        presets.Should().Contain(preset =>
            preset.Id == "qwen36-27b-q3km" &&
            preset.ModelScopeModelId == "unsloth/Qwen3.6-27B-GGUF" &&
            preset.FileName == "Qwen3.6-27B-Q3_K_M.gguf" &&
            preset.FileSizeBytes == 13586217184 &&
            preset.Sha256 == "bdfa99d488b501f5ca711f35c930f005319a064eabcaae23290571a25c499eeb" &&
            preset.License == "apache-2.0" &&
            preset.UseCase.Contains("英翻中", StringComparison.Ordinal));
        presets.Should().Contain(preset =>
            preset.Id == "qwen36-27b-iq4xs" &&
            preset.FileName == "Qwen3.6-27B-IQ4_XS.gguf" &&
            preset.FileSizeBytes == 15440005344);
        presets.Should().Contain(preset =>
            preset.Id == "qwen35-9b-q8" &&
            preset.ModelScopeModelId == "unsloth/Qwen3.5-9B-GGUF" &&
            preset.FileName == "Qwen3.5-9B-Q8_0.gguf" &&
            preset.Sha256 == "809626574d0cb43d4becfa56169980da2bb448f2299270f7be443cb89d0a6ae4");
        presets.Should().Contain(preset =>
            preset.Id == "sakura-14b-qwen3-q6k" &&
            preset.ModelScopeModelId == "sakuraumi/Sakura-14B-Qwen3-v1.5-GGUF" &&
            preset.FileName == "sakura-14b-qwen3-v1.5-q6k.gguf" &&
            preset.License == "CC-BY-NC-SA-4.0 / 非商用" &&
            preset.UseCase.Contains("日翻中", StringComparison.Ordinal));
        presets.Should().Contain(preset =>
            preset.Id == "qwen3-14b-q4km" &&
            preset.ModelScopeModelId == "Qwen/Qwen3-14B-GGUF" &&
            preset.FileName == "Qwen3-14B-Q4_K_M.gguf" &&
            preset.FileSizeBytes == 9001752960);
    }

    [Fact]
    public void BuiltInPresets_build_modelscope_resolve_download_urls()
    {
        var preset = LlamaCppModelDownloadPresets.All.Single(item => item.Id == "qwen35-9b-q8");

        preset.DownloadUrl.Should().Be("https://www.modelscope.cn/models/unsloth/Qwen3.5-9B-GGUF/resolve/master/Qwen3.5-9B-Q8_0.gguf");
    }

    [Fact]
    public void StartDownload_reuses_existing_verified_file_without_network_request()
    {
        using var temp = new TemporaryDirectory();
        var payload = Encoding.UTF8.GetBytes("small gguf");
        var preset = CreatePreset("reuse", payload);
        var targetDirectory = Path.Combine(temp.Path, preset.Id);
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, preset.FileName);
        File.WriteAllBytes(targetPath, payload);
        using var client = new HttpClient(new ThrowingHandler());
        var manager = new LlamaCppModelDownloadManager(client, temp.Path, new[] { preset });

        var status = manager.StartDownload(preset.Id);

        status.State.Should().Be("completed");
        status.LocalPath.Should().Be(targetPath);
        status.DownloadedBytes.Should().Be(payload.Length);
        status.Message.Should().Contain("已存在");
    }

    [Fact]
    public async Task StartDownload_streams_to_part_file_and_moves_verified_file()
    {
        using var temp = new TemporaryDirectory();
        var payload = Encoding.UTF8.GetBytes("downloaded gguf");
        var preset = CreatePreset("fresh", payload);
        using var client = new HttpClient(new StaticContentHandler(payload));
        var manager = new LlamaCppModelDownloadManager(client, temp.Path, new[] { preset });

        manager.StartDownload(preset.Id);
        var status = await WaitForTerminalStatusAsync(manager);

        status.State.Should().Be("completed");
        status.DownloadedBytes.Should().Be(payload.Length);
        File.ReadAllBytes(status.LocalPath!).Should().Equal(payload);
        File.Exists(status.LocalPath + ".part").Should().BeFalse();
    }

    [Fact]
    public async Task StartDownload_deletes_part_file_when_sha256_does_not_match()
    {
        using var temp = new TemporaryDirectory();
        var payload = Encoding.UTF8.GetBytes("bad gguf");
        var preset = new LlamaCppModelDownloadPreset(
            Id: "bad",
            Label: "Bad",
            ModelScopeModelId: "owner/bad",
            FileName: "bad.gguf",
            FileSizeBytes: payload.Length,
            Sha256: new string('0', 64),
            Quantization: "Q4",
            UseCase: "测试",
            License: "test",
            Notes: "test");
        using var client = new HttpClient(new StaticContentHandler(payload));
        var manager = new LlamaCppModelDownloadManager(client, temp.Path, new[] { preset });

        manager.StartDownload(preset.Id);
        var status = await WaitForTerminalStatusAsync(manager);

        status.State.Should().Be("error");
        status.Message.Should().Contain("SHA256");
        File.Exists(Path.Combine(temp.Path, preset.Id, preset.FileName)).Should().BeFalse();
        File.Exists(Path.Combine(temp.Path, preset.Id, preset.FileName + ".part")).Should().BeFalse();
    }

    [Fact]
    public async Task StartDownload_rejects_second_download_while_first_is_running()
    {
        using var temp = new TemporaryDirectory();
        using var gate = new ManualResetEventSlim(false);
        var firstPayload = Encoding.UTF8.GetBytes("first");
        var secondPayload = Encoding.UTF8.GetBytes("second");
        var first = CreatePreset("first", firstPayload);
        var second = CreatePreset("second", secondPayload);
        using var client = new HttpClient(new BlockingHandler(firstPayload, gate));
        var manager = new LlamaCppModelDownloadManager(client, temp.Path, new[] { first, second });

        manager.StartDownload(first.Id);
        await WaitForStateAsync(manager, "downloading");
        var secondStatus = manager.StartDownload(second.Id);
        gate.Set();
        await WaitForTerminalStatusAsync(manager);

        secondStatus.State.Should().Be("downloading");
        secondStatus.Message.Should().Contain("已有模型下载任务");
        File.Exists(Path.Combine(temp.Path, second.Id, second.FileName)).Should().BeFalse();
    }

    [Fact]
    public async Task CancelDownload_marks_active_download_as_cancelled()
    {
        using var temp = new TemporaryDirectory();
        using var gate = new ManualResetEventSlim(false);
        var payload = Encoding.UTF8.GetBytes("cancel");
        var preset = CreatePreset("cancel", payload);
        using var client = new HttpClient(new BlockingHandler(payload, gate));
        var manager = new LlamaCppModelDownloadManager(client, temp.Path, new[] { preset });

        manager.StartDownload(preset.Id);
        await WaitForStateAsync(manager, "downloading");
        var cancelStatus = manager.CancelDownload();
        gate.Set();
        var terminal = await WaitForTerminalStatusAsync(manager);

        cancelStatus.State.Should().Be("cancelled");
        terminal.State.Should().Be("cancelled");
        File.Exists(Path.Combine(temp.Path, preset.Id, preset.FileName + ".part")).Should().BeFalse();
    }

    private static LlamaCppModelDownloadPreset CreatePreset(string id, byte[] payload)
    {
        return new LlamaCppModelDownloadPreset(
            Id: id,
            Label: id,
            ModelScopeModelId: $"owner/{id}",
            FileName: $"{id}.gguf",
            FileSizeBytes: payload.Length,
            Sha256: Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            Quantization: "Q4",
            UseCase: "测试",
            License: "test",
            Notes: "test");
    }

    private static async Task<LlamaCppModelDownloadStatus> WaitForStateAsync(
        LlamaCppModelDownloadManager manager,
        string state)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var status = manager.GetStatus();
            if (status.State == state)
            {
                return status;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {state}. Last state: {manager.GetStatus().State}");
    }

    private static async Task<LlamaCppModelDownloadStatus> WaitForTerminalStatusAsync(LlamaCppModelDownloadManager manager)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var status = manager.GetStatus();
            if (status.State is "completed" or "error" or "cancelled")
            {
                return status;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for terminal status. Last state: {manager.GetStatus().State}");
    }

    private sealed class StaticContentHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public StaticContentHandler(byte[] payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            });
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly ManualResetEventSlim _gate;

        public BlockingHandler(byte[] payload, ManualResetEventSlim gate)
        {
            _payload = payload;
            _gate = gate;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingReadStream(_payload, _gate))
            });
        }
    }

    private sealed class BlockingReadStream : MemoryStream
    {
        private readonly ManualResetEventSlim _gate;
        private bool _blocked;

        public BlockingReadStream(byte[] buffer, ManualResetEventSlim gate)
            : base(buffer)
        {
            _gate = gate;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_blocked)
            {
                _blocked = true;
                _gate.Wait();
            }

            return base.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.Run(() => Read(buffer, offset, count), cancellationToken);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Network should not be used.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hunity-model-download-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
