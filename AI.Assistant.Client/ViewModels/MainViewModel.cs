using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;
using AI.Assistant.Core.Rag.Interfaces;
using AI.Assistant.Core.Rag.Models;
using AI.Assistant.Infrastructure.Services;
using AI.Assistant.Infrastructure.Services.Chat;
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
    private readonly RagChatService _ragService;
    private readonly IConversationStore _convStore;

    /// <summary>当前会话切换事件</summary>
    public event EventHandler<ConversationViewModel>? ConversationChanged;

    [ObservableProperty]
    private ConversationViewModel? _currentConversation;

    [ObservableProperty]
    private bool _isPlaygroundMode;

    public bool HasPlayground { get; }

    public bool ShowRagDetails { get; }

    [ObservableProperty]
    private bool _isRagEnabled = true;

    partial void OnIsRagEnabledChanged(bool value)
    {
        _ragService.IsRagEnabled = value;
    }

    public ObservableCollection<ConversationViewModel> Conversations { get; } = [];

    public ObservableCollection<KnowledgeSource> Sources { get; } = [];

    public MainViewModel(
        IChatService chatService,
        IWorkspaceManager workspace,
        IEnumerable<IDocumentParser> parsers,
        IKnowledgeStore knowledgeStore,
        IIndexer indexer,
        RagChatService ragService,
        IConversationStore conversationStore,
        MemoryService? memory = null,
        bool isDevMode = true)
    {
        _chatService = chatService;
        _workspace = workspace;
        _parsers = parsers;
        _knowledgeStore = knowledgeStore;
        _indexer = indexer;
        _ragService = ragService;
        _convStore = conversationStore;
        _memory = memory;
        HasPlayground = isDevMode;
        ShowRagDetails = isDevMode;

        LoadSavedConversations();

        foreach (var source in _workspace.GetSources())
            Sources.Add(source);
    }

    private void LoadSavedConversations()
    {
        try
        {
            var saved = _convStore.LoadIndexAsync().GetAwaiter().GetResult();

            foreach (var conv in saved)
            {
                var vm = new ConversationViewModel(_chatService!, _memory, ShowRagDetails, _convStore);

                // 直接加载完整会话（含消息）
                var full = _convStore.LoadConversationAsync(conv.Id.ToString()).GetAwaiter().GetResult();
                vm.LoadFrom(full ?? conv);

                Conversations.Add(vm);
            }

            if (Conversations.Count > 0)
            {
                CurrentConversation = Conversations[0];
                Conversations[0].IsSelected = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainVM] 加载会话失败: {ex}");
        }

        if (Conversations.Count == 0)
        {
            NewConversation();
        }
    }

    partial void OnCurrentConversationChanged(ConversationViewModel? oldValue, ConversationViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null)
        {
            newValue.IsSelected = true;
            _ = LoadConversationMessagesAsync(newValue);
            ConversationChanged?.Invoke(this, newValue);
        }
    }

    private async Task LoadConversationMessagesAsync(ConversationViewModel vm)
    {
        if (vm.MessageCount > 0) return;

        var full = await _convStore.LoadConversationAsync(vm.ConversationId);
        if (full is not null && full.Messages.Count > 0)
        {
            vm.LoadFrom(full);
        }
    }

    [RelayCommand]
    private void NewConversation()
    {
        var conversation = _chatService is not null
            ? new ConversationViewModel(_chatService, _memory, ShowRagDetails, _convStore)
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

        _ = _convStore.DeleteConversationAsync(conversation.ConversationId);
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
            Filter = "文档文件 (*.md;*.txt;*.pdf)|*.md;*.txt;*.pdf|Markdown (*.md)|*.md|文本文件 (*.txt)|*.txt|PDF 文件 (*.pdf)|*.pdf|所有文件 (*.*)|*.*",
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

            if (chunks.Count == 0)
            {
                source.IndexStatus = "无内容";
                return;
            }

            await _knowledgeStore.SaveChunksAsync(chunks);
            source.IndexStatus = "已索引";
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException is not null
                ? $"{ex.Message}\n---\n{ex.InnerException.Message}"
                : ex.Message;
            System.Diagnostics.Debug.WriteLine($"[上传PDF] 失败: {ex}");
            source.IndexStatus = $"{Path.GetFileName(source.Uri)} 索引失败";
            System.Windows.MessageBox.Show(msg, "索引失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RenameConversation(ConversationViewModel conversation)
    {
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = conversation.Title,
            FontSize = 14,
            Margin = new System.Windows.Thickness(0, 0, 0, 12),
            MinWidth = 300
        };
        textBox.Focus();

        Window? window = null;
        window = new Window
        {
            Title = "重命名对话",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            Content = new StackPanel
            {
                Margin = new System.Windows.Thickness(16),
                Children =
                {
                    new System.Windows.Controls.TextBlock
                    {
                        Text = "输入新名称:",
                        FontSize = 13,
                        Margin = new System.Windows.Thickness(0, 0, 0, 8)
                    },
                    textBox,
                    new System.Windows.Controls.Button
                    {
                        Content = "确定",
                        Width = 80,
                        Height = 30,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                        IsDefault = true,
                        Command = new RelayCommand(() =>
                        {
                            var name = textBox.Text.Trim();
                            if (name.Length > 0)
                                conversation.Title = name;
                            window!.Close();
                        })
                    }
                }
            }
        };

        window.ShowDialog();
    }
}
