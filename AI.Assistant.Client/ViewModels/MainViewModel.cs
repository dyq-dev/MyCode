using System.Collections.ObjectModel;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI.Assistant.Client.ViewModels;

/// <summary>
/// 主窗口 ViewModel - 管理会话列表和当前选中的会话
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IChatService? _chatService;
    private readonly MemoryService? _memory;

    /// <summary>当前会话切换事件</summary>
    public event EventHandler<ConversationViewModel>? ConversationChanged;

    [ObservableProperty]
    private ConversationViewModel? _currentConversation;

    public ObservableCollection<ConversationViewModel> Conversations { get; } = [];

    public MainViewModel(IChatService chatService, MemoryService? memory = null)
    {
        _chatService = chatService;
        _memory = memory;
        NewConversation();
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
}
