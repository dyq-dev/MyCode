using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AI.Assistant.Client.ViewModels;

/// <summary>
/// 主窗口 ViewModel - 管理会话列表、知识源和当前选中的会话
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IChatService? _chatService;
    private readonly MemoryService? _memory;
    private readonly IWorkspaceManager _workspace;
    private readonly IEnumerable<IDocumentParser> _parsers;
    private readonly IKnowledgeStore _knowledgeStore;
    private readonly IIndexer _indexer;

    /// <summary>当前会话切换事件</summary>
    public event EventHandler<ConversationViewModel>? ConversationChanged;

    [ObservableProperty]
    private ConversationViewModel? _currentConversation;

    [ObservableProperty]
    private bool _isPlaygroundMode;

    public ObservableCollection<ConversationViewModel> Conversations { get; } = [];

    public ObservableCollection<KnowledgeSource> Sources { get; } = [];

    public MainViewModel(
        IChatService chatService,
        IWorkspaceManager workspace,
        IEnumerable<IDocumentParser> parsers,
        IKnowledgeStore knowledgeStore,
        IIndexer indexer,
        MemoryService? memory = null)
    {
        _chatService = chatService;
        _workspace = workspace;
        _parsers = parsers;
        _knowledgeStore = knowledgeStore;
        _indexer = indexer;
        _memory = memory;
        NewConversation();

        foreach (var source in _workspace.GetSources())
            Sources.Add(source);
    }

    partial void OnCurrentConversationChanged(ConversationViewModel? oldValue, ConversationViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null)
        {
            newValue.IsSelected = true;
            ConversationChanged?.Invoke(this, newValue);
        }
    }

    [RelayCommand]
    private void NewConversation()
    {
        var conversation = _chatService is not null
            ? new ConversationViewModel(_chatService, _memory)
            : new ConversationViewModel();
        conversation.Title = $"新对话 {Conversations.Count + 1}";
        Conversations.Insert(0, conversation);
        CurrentConversation = conversation;
    }

    [RelayCommand]
    private void SelectConversation(ConversationViewModel conversation)
    {
        CurrentConversation = conversation;
    }

    [RelayCommand]
    private void DeleteConversation(ConversationViewModel conversation)
    {
        Conversations.Remove(conversation);
        if (CurrentConversation == conversation)
        {
            CurrentConversation = Conversations.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void TogglePlayground()
    {
        IsPlaygroundMode = !IsPlaygroundMode;
    }

    [RelayCommand]
    private async Task ReindexSourceAsync(KnowledgeSource source)
    {
        source.IndexStatus = "索引中";

        try
        {
            if (source.SourceType == SourceType.Code)
            {
                await _indexer.IndexSourceAsync(source.Uri);
            }
            else
            {
                var ext = Path.GetExtension(source.Uri);
                var parser = _parsers.FirstOrDefault(p =>
                    p.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
                if (parser is null)
                {
                    source.IndexStatus = "不支持的格式";
                    return;
                }

                var chunks = new List<KnowledgeChunk>();
                await foreach (var chunk in parser.ParseAsync(source.Uri))
                {
                    chunk.SourceId = source.Id;
                    chunk.SourceUri = source.Uri;
                    chunk.ProjectPath = source.Uri;
                    chunks.Add(chunk);
                }

                await _knowledgeStore.SaveChunksAsync(chunks);
            }

            source.IndexStatus = "已索引";
        }
        catch
        {
            source.IndexStatus = "失败";
        }
    }

    [RelayCommand]
    private async Task DeleteSourceAsync(KnowledgeSource source)
    {
        var result = MessageBox.Show(
            $"确定要删除源 \"{source.Name}\" 及其所有索引数据吗？",
            "删除知识源",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        // 先从 Qdrant 清理（容错，失败不影响 UI 移除）
        try
        {
            await _knowledgeStore.DeleteChunksBySourceAsync(source.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeleteSource] Qdrant 清理失败: {ex.Message}");
        }

        _workspace.RemoveSource(source.Id);
        var item = Sources.FirstOrDefault(s => s.Id == source.Id);
        if (item is not null)
            Sources.Remove(item);
    }

    [RelayCommand]
    private async Task AddDocumentAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "文档文件 (*.md;*.txt)|*.md;*.txt|Markdown (*.md)|*.md|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

        var filePath = dialog.FileName;
        var ext = Path.GetExtension(filePath);

        var parser = _parsers.FirstOrDefault(p =>
            p.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
        if (parser is null) return;

        var source = new KnowledgeSource
        {
            Name = Path.GetFileName(filePath),
            SourceType = parser.SourceType,
            Uri = filePath,
            IndexStatus = "索引中"
        };

        _workspace.AddSource(source);
        Sources.Add(source);

        try
        {
            var chunks = new List<KnowledgeChunk>();
            await foreach (var chunk in parser.ParseAsync(filePath))
            {
                chunk.SourceId = source.Id;
                chunk.SourceUri = filePath;
                chunk.ProjectPath = filePath;
                chunks.Add(chunk);
            }

            await _knowledgeStore.SaveChunksAsync(chunks);
            source.IndexStatus = "已索引";
        }
        catch
        {
            source.IndexStatus = "失败";
        }
    }
}
