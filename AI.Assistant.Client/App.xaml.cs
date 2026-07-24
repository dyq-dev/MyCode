using System.Windows;
using AI.Assistant.Client.ViewModels;
using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Options;
using AI.Assistant.Infrastructure.Extensions;
using AI.Assistant.Infrastructure.Services.Rag.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AI.Assistant.Client;

public partial class App : Application
{
    private readonly IHost _host;

    /// <summary>DI 容器访问入口（供 UserControl 解析 ViewModel）</summary>
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
                    // Provider 选择
                    options.ChatProvider = c["LLM:ChatProvider"] ?? "Ollama";
                    options.EmbeddingProvider = c["LLM:EmbeddingProvider"] ?? "Ollama";

                    // Ollama 本地
                    options.OllamaBaseUrl = c["LLM:Ollama:BaseUrl"] ?? "http://localhost:11434";
                    options.OllamaChatModel = c["LLM:Ollama:ChatModel"] ?? "gemma3:1b";
                    options.OllamaEmbeddingModel = c["LLM:Ollama:EmbeddingModel"] ?? "";

                    // Chat 云端（独立厂商/Key）
                    options.ChatCloudBaseUrl = c["LLM:Cloud:BaseUrl"] ?? "";
                    options.ChatCloudApiKey = c["LLM:Cloud:ApiKey"] ?? "";
                    options.ChatCloudModel = c["LLM:Cloud:Model"] ?? "";

                    // Embedding 云端（独立厂商/Key）
                    options.EmbeddingCloudBaseUrl = c["LLM:EmbeddingCloud:BaseUrl"] ?? "";
                    options.EmbeddingCloudApiKey = c["LLM:EmbeddingCloud:ApiKey"] ?? "";
                    options.EmbeddingCloudModel = c["LLM:EmbeddingCloud:Model"] ?? "";

                    // Qdrant
                    options.QdrantBaseUrl = c["Qdrant:BaseUrl"] ?? "http://localhost:6333";
                    options.QdrantCollection = c["Qdrant:Collection"] ?? "memories";
                    options.SqlConnectionString = c["Sql:ConnectionString"]
                        ?? "Server=localhost;Database=AIAssistant;Trusted_Connection=True;TrustServerCertificate=True;";
                });

                // RAG 服务（装饰 ChatService，注入代码上下文）
                services.AddRag(o =>
                {
                    o.EnableDebugInfo = true;
                    o.EnableDebugLog = true;
                    o.MinimumScoreThreshold = 0.3;
                });
                services.AddRagChatIntegration();

                services.AddSingleton<MainViewModel>();
                services.AddTransient<ConversationViewModel>();
                services.AddSingleton<Views.MainWindow>();
                services.AddSingleton<KnowledgePlaygroundViewModel>();
            })
            .Build();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<Views.MainWindow>();
        mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        mainWindow.Show();

        // 后台初始化长期记忆存储，不阻塞 UI 启动；
        // 若 mssql/Qdrant 不可用，仅记忆功能降级，不影响聊天。
        var memory = _host.Services.GetService<AI.Assistant.Infrastructure.Services.MemoryService>();
        if (memory is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await memory.EnsureReadyAsync();
                }
                catch (Exception ex)
                {
                    // 记录但不抛出，避免影响主流程
                    System.Diagnostics.Debug.WriteLine($"[Memory] 初始化失败（记忆功能不可用）: {ex.Message}");
                }
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
