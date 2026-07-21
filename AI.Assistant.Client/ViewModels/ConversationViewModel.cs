using System.Collections.ObjectModel;
using System.Windows.Threading;
using AI.Assistant.Core.Interfaces;
using AI.Assistant.Core.Models;
using AI.Assistant.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AI.Assistant.Client.ViewModels;

/// <summary>
/// 单个会话的 ViewModel - 管理消息列表和发送逻辑
/// </summary>
public partial class ConversationViewModel : ObservableObject
{
    private readonly IChatService? _chatService;
    private readonly MemoryService? _memory;
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private CancellationTokenSource? _streamCts;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _typeTimer;
    private readonly Queue<char> _charQueue = new();
    private ChatMessageViewModel? _activeAssistantMessage;
    private ChatMessageViewModel? _waitingMessage;
    private DateTime _waitStartTime;

    /// <summary>触发滚动到底部</summary>
    public event EventHandler? ScrollToBottomRequested;

    /// <summary>忙碌状态变化事件</summary>
    public event EventHandler<bool>? IsBusyChanged;

    /// <summary>内容更新事件（流式回复时触发）</summary>
    public event EventHandler? ContentUpdated;

    [ObservableProperty]
    private string _title = "新对话";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnIsBusyChanging(bool oldValue, bool newValue)
    {
        IsBusyChanged?.Invoke(this, newValue);
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ConversationViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;

        // 打字机效果：每隔固定间隔从队列弹出一个字符追加到回复内容
        _typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _typeTimer.Tick += TypeTimer_Tick;
    }

    public ConversationViewModel(IChatService chatService, MemoryService? memory = null) : this()
    {
        _chatService = chatService;
        _memory = memory;
    }

    /// <summary>
    /// 定时器触发 - 每秒更新等待消息的时间显示
    /// </summary>
    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_waitingMessage is not null)
        {
            var elapsed = DateTime.Now - _waitStartTime;
            _waitingMessage.DisplayTime = $"{elapsed.Seconds}s";
        }
    }

    /// <summary>
    /// 开始计时
    /// </summary>
    private void StartWaiting(ChatMessageViewModel message)
    {
        _waitingMessage = message;
        _waitStartTime = DateTime.Now;
        message.DisplayTime = "0s";
        _timer.Start();
    }

    /// <summary>触发滚动到底部</summary>
    private void RequestScroll()
    {
        ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 构建发送给模型的对话历史，注入三层长期记忆（摘要 → 事实 → 原文）。
    /// </summary>
    private async Task<IEnumerable<ChatMessage>> BuildHistoryWithMemoryAsync(ChatMessageViewModel currentAssistant, string userText, ChatMessageViewModel currentUserMessage)
    {
        var history = Messages
            .Where(m => m != currentAssistant && m != currentUserMessage)
            .Select(m => new ChatMessage
            {
                Content = m.Content,
                Role = m.Role,
                Timestamp = m.Timestamp
            })
            .ToList();

        if (_memory is null)
            return history;

        var result = await _memory.RetrieveAsync(userText, _sessionId);
        if (result.Summary is null && result.Facts.Count == 0 && result.RawTexts.Count == 0)
            return history;

        var parts = new List<string>();
        if (result.Summary is not null)
            parts.Add($"会话摘要：{result.Summary}");
        if (result.Facts.Count > 0)
            parts.Add($"已知事实：\n- {string.Join("\n- ", result.Facts)}");
        if (result.RawTexts.Count > 0)
            parts.Add($"相关历史：\n- {string.Join("\n- ", result.RawTexts)}");

        var memoryContext = "以下是你已知的关于用户的信息（必须据此回答，不知道就说不知道）：\n" + string.Join("\n\n", parts);
        history.Insert(0, new ChatMessage
        {
            Content = memoryContext,
            Role = MessageRole.System,
            Timestamp = DateTime.Now
        });

        return history;
    }

    /// <summary>
    /// 把文本按字符送入打字机队列，并启动打字机定时器。
    /// 无论服务端一次返回多少字，UI 都逐字显示。
    /// </summary>
    private void EnqueueTypewriter(ChatMessageViewModel message, string text)
    {
        _activeAssistantMessage = message;
        foreach (var c in text)
            _charQueue.Enqueue(c);

        if (!_typeTimer.IsEnabled)
            _typeTimer.Start();
    }

    /// <summary>
    /// 打字机定时器：每次弹出一个字符追加到当前回复，并通知 UI 更新。
    /// </summary>
    private void TypeTimer_Tick(object? sender, EventArgs e)
    {
        if (_charQueue.Count == 0)
        {
            _typeTimer.Stop();
            return;
        }

        var message = _activeAssistantMessage;
        if (message is null)
        {
            _charQueue.Clear();
            _typeTimer.Stop();
            return;
        }

        message.Content += _charQueue.Dequeue();
        ContentUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 等待打字机把队列中剩余的字符全部显示完毕。
    /// 用较短轮询间隔，避免回复结束时拖沓。
    /// </summary>
    private async Task WaitForTypewriterAsync(CancellationToken cancellationToken)
    {
        while (_charQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(10, cancellationToken);
        }
        FlushTypewriter();
    }

    /// <summary>
    /// 立即把队列里剩余字符一次性补全（取消/出错/结束时调用）。
    /// </summary>
    private void FlushTypewriter()
    {
        _typeTimer.Stop();
        if (_activeAssistantMessage is not null && _charQueue.Count > 0)
        {
            _activeAssistantMessage.Content += new string(_charQueue.ToArray());
            ContentUpdated?.Invoke(this, EventArgs.Empty);
        }
        _charQueue.Clear();
    }

    /// <summary>
    /// 停止计时，显示最终耗时
    /// </summary>
    private void StopWaiting(ChatMessageViewModel message, bool hasContent)
    {
        _timer.Stop();
        if (_waitingMessage == message)
        {
            _waitingMessage = null;
        }

        if (hasContent)
        {
            var elapsed = DateTime.Now - _waitStartTime;
            message.DisplayTime = elapsed.TotalSeconds < 1
                ? "<1s"
                : $"{elapsed.TotalSeconds:F1}s";
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsBusy)
            return;

        var userMessage = new ChatMessageViewModel
        {
            Content = InputText,
            Role = MessageRole.User,
            Timestamp = DateTime.Now
        };
        Messages.Add(userMessage);
        RequestScroll();

        var messageText = InputText;
        InputText = string.Empty;

        // Demo 模式
        if (_chatService is null)
        {
            IsBusy = true;
            var demoMsg = new ChatMessageViewModel
            {
                Content = string.Empty,
                Role = MessageRole.Assistant,
                Timestamp = DateTime.Now
            };
            Messages.Add(demoMsg);
            StartWaiting(demoMsg);
            await Task.Delay(1500);
            EnqueueTypewriter(demoMsg, $"[Demo] 收到消息: {messageText}");
            StopWaiting(demoMsg, true);
            IsBusy = false;
            return;
        }

        // 真实 AI 服务 - 流式响应
        IsBusy = true;
        _streamCts = new CancellationTokenSource();

        var assistantMessage = new ChatMessageViewModel
        {
            Content = string.Empty,
            Role = MessageRole.Assistant,
            Timestamp = DateTime.Now
        };
        Messages.Add(assistantMessage);
        StartWaiting(assistantMessage);

        try
        {
            // 长期记忆检索：把相关历史记忆作为 system 上下文注入
            var history = await BuildHistoryWithMemoryAsync(assistantMessage, messageText, userMessage);

            await foreach (var chunk in _chatService.StreamAsync(messageText, history, _streamCts.Token))
            {
                // 不直接拼接，而是入队，由打字机定时器逐字显示
                EnqueueTypewriter(assistantMessage, chunk);
            }

            // 等待打字机把队列里的字全部显示完
            await WaitForTypewriterAsync(_streamCts.Token);

            StopWaiting(assistantMessage, assistantMessage.Content.Length > 0);

            // 后台存储记忆并提取事实 + 更新会话摘要，不阻塞 UI
            if (_memory is not null)
            {
                _ = _memory.StoreAsync(messageText, "user", _sessionId,
                    assistantResponse: assistantMessage.Content,
                    cancellationToken: _streamCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            FlushTypewriter();
            StopWaiting(assistantMessage, assistantMessage.Content.Length > 0);
            if (assistantMessage.Content.Length == 0)
            {
                Messages.Remove(assistantMessage);
            }
        }
        catch (Exception ex)
        {
            FlushTypewriter();
            StopWaiting(assistantMessage, true);
            assistantMessage.Content = $"错误: {ex.Message}";
            assistantMessage.Role = MessageRole.System;
        }
        finally
        {
            IsBusy = false;
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    [RelayCommand]
    private void CancelStream()
    {
        _streamCts?.Cancel();
    }
}

/// <summary>
/// 单条消息的 ViewModel
/// </summary>
public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private MessageRole _role;

    [ObservableProperty]
    private DateTime _timestamp;

    /// <summary>显示时间（等待中显示秒数，完成后显示耗时）</summary>
    [ObservableProperty]
    private string _displayTime = string.Empty;

    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
    public bool IsSystem => Role == MessageRole.System;
}
