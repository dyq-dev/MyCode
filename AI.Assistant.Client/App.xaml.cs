using System.IO;
using System.Windows;
using AI.Assistant.Client.ViewModels;
using AI.Assistant.Core.Rag.Context;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
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
                    o.ProjectPath = Environment.CurrentDirectory;
                    o.RagKeywords = [.. o.RagKeywords, "怎么", "什么", "做法", "步骤", "内容"];
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

        // 自动注册当前项目为 KnowledgeSource（暂未启用）
        //try
        //{
        //    var workspace = _host.Services.GetRequiredService<IWorkspaceManager>();
        //    var options = _host.Services.GetRequiredService<RagOptions>();
        //    var projectPath = options.ProjectPath;
        //    if (string.IsNullOrEmpty(projectPath))
        //        projectPath = Environment.CurrentDirectory;
        //
        //    if (workspace.GetSourceByUri(projectPath) is null)
        //    {
        //        workspace.AddSource(new KnowledgeSource
        //        {
        //            Name = "当前项目",
        //            SourceType = SourceType.Code,
        //            Uri = projectPath,
        //            AutoSync = true,
        //            IndexStatus = "未索引"
        //        });
        //    }
        //}
        //catch (Exception ex)
        //{
        //    System.Diagnostics.Debug.WriteLine($"[Workspace] 自动注册失败: {ex.Message}");
        //}

        // 加载已持久化的 Workspace Source（同步等待，确保 MainWindow 打开前数据就绪）
        try
        {
            var workspace = _host.Services.GetRequiredService<IWorkspaceManager>();
            workspace.LoadAsync().GetAwaiter().GetResult();

            // 文档源自动触发首次索引（后台异步执行，不阻塞 UI）
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
            System.Diagnostics.Debug.WriteLine($"[Workspace] 启动加载失败: {ex.Message}");
        }

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
