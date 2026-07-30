using System.Diagnostics;
using System.IO;
using System.Windows;
using AI.Assistant.Client.ViewModels;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Extensions;
using AI.Assistant.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AI.Assistant.Client;

public partial class App : Application
{
    private readonly IHost _host;

    public static IServiceProvider Services => ((App)Current)._host.Services;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.Sources.Clear();
                config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                var c = context.Configuration;

                services.AddInfrastructure(options =>
                {
                    options.ChatProvider = c["LLM:ChatProvider"] ?? "Ollama";
                    options.EmbeddingProvider = c["LLM:EmbeddingProvider"] ?? "Ollama";
                    options.OllamaBaseUrl = c["LLM:Ollama:BaseUrl"] ?? "http://localhost:11434";
                    options.OllamaChatModel = c["LLM:Ollama:ChatModel"] ?? "gemma3:1b";
                    options.OllamaEmbeddingModel = c["LLM:Ollama:EmbeddingModel"] ?? "";
                    options.ChatCloudBaseUrl = c["LLM:Cloud:BaseUrl"] ?? "";
                    options.ChatCloudApiKey = c["LLM:Cloud:ApiKey"] ?? "";
                    options.ChatCloudModel = c["LLM:Cloud:Model"] ?? "";
                    options.EmbeddingCloudBaseUrl = c["LLM:EmbeddingCloud:BaseUrl"] ?? "";
                    options.EmbeddingCloudApiKey = c["LLM:EmbeddingCloud:ApiKey"] ?? "";
                    options.EmbeddingCloudModel = c["LLM:EmbeddingCloud:Model"] ?? "";
                    options.QdrantBaseUrl = c["Qdrant:BaseUrl"] ?? "http://localhost:6333";
                    options.QdrantCollection = c["Qdrant:Collection"] ?? "memories";
                    options.SqlConnectionString = c["Sql:ConnectionString"]
                        ?? "Server=localhost;Database=AIAssistant;Trusted_Connection=True;TrustServerCertificate=True;";
                });

                services.AddRag(o =>
                {
                    o.EnableDebugInfo = true;
                    o.EnableDebugLog = true;
                    o.MinimumScoreThreshold = 0.3;
                    o.ProjectPath = Environment.CurrentDirectory;
                    o.RagKeywords = [.. o.RagKeywords, "怎么", "什么", "做法", "步骤", "内容"];
                });
                services.AddRagChatIntegration();

                var isDevMode = string.Equals(c["DevMode"], "true", StringComparison.OrdinalIgnoreCase);

                services.AddSingleton<MainViewModel>(sp =>
                {
                    var chatService = sp.GetRequiredService<IChatService>();
                    var workspace = sp.GetRequiredService<IWorkspaceManager>();
                    var parsers = sp.GetRequiredService<IEnumerable<IDocumentParser>>();
                    var knowledgeStore = sp.GetRequiredService<IKnowledgeStore>();
                    var indexer = sp.GetRequiredService<IIndexer>();
                    var memory = sp.GetService<MemoryService>();
                    return new MainViewModel(chatService, workspace, parsers, knowledgeStore, indexer, memory, isDevMode);
                });
                services.AddTransient<ConversationViewModel>();
                services.AddSingleton<Views.MainWindow>();
                services.AddSingleton<KnowledgePlaygroundViewModel>();
            })
            .Build();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var workspace = _host.Services.GetRequiredService<IWorkspaceManager>();
            var store = _host.Services.GetRequiredService<IWorkspaceStore>();
            var loaded = Task.Run(() => store.LoadAsync().GetAwaiter().GetResult()).GetAwaiter().GetResult();
            foreach (var s in loaded)
                workspace.AddSource(s);

            var parsers = _host.Services.GetRequiredService<IEnumerable<IDocumentParser>>();
            var knowledgeStore = _host.Services.GetRequiredService<IKnowledgeStore>();
            var docSources = workspace.GetSources()
                .Where(s => s.SourceType is SourceType.Document or SourceType.Markdown
                    or SourceType.Text or SourceType.Pdf)
                .ToList();

            foreach (var source in docSources)
            {
                var captured = source;
                _ = Task.Run(async () =>
                {
                    captured.IndexStatus = "索引中";
                    try
                    {
                        var ext = Path.GetExtension(captured.Uri);
                        var parser = parsers.FirstOrDefault(p =>
                            p.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
                        if (parser is null)
                        {
                            captured.IndexStatus = "不支持的格式";
                            return;
                        }

                        var chunks = new List<KnowledgeChunk>();
                        await foreach (var chunk in parser.ParseAsync(captured.Uri))
                        {
                            chunk.SourceId = captured.Id;
                            chunk.SourceUri = captured.Uri;
                            chunk.ProjectPath = captured.Uri;
                            chunks.Add(chunk);
                        }

                        await knowledgeStore.SaveChunksAsync(chunks);
                        captured.IndexStatus = "已索引";
                    }
                    catch
                    {
                        captured.IndexStatus = "失败";
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] 启动加载 Workspace 失败: {ex.Message}");
        }

        var mainWindow = _host.Services.GetRequiredService<Views.MainWindow>();
        mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        mainWindow.Show();

        var memory = _host.Services.GetService<MemoryService>();
        if (memory is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await memory.EnsureReadyAsync(); }
                catch (Exception ex) { Debug.WriteLine($"[Memory] {ex.Message}"); }
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
