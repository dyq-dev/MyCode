using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AI.Assistant.Client.ViewModels;

namespace AI.Assistant.Client.Views;

public partial class MainWindow : Window
{
    private INotifyCollectionChanged? _currentCollection;
    private ConversationViewModel? _currentConversation;

    // 自动贴底开关：流式输出期间为 true，用户向上滚动查看历史时置为 false，
    // 当用户再次滚回底部时恢复为 true。
    private bool _autoScroll = true;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MessagesScrollViewer.ScrollChanged += OnScrollChanged;

        DataContextChanged += (s, args) =>
        {
            if (args.OldValue is MainViewModel oldVm)
                oldVm.ConversationChanged -= OnConversationChanged;
            if (args.NewValue is MainViewModel newVm)
                newVm.ConversationChanged += OnConversationChanged;
        };

        if (DataContext is MainViewModel vm)
            vm.ConversationChanged += OnConversationChanged;
    }

    private void OnConversationChanged(object? sender, ConversationViewModel e)
    {
        if (_currentCollection is not null)
            _currentCollection.CollectionChanged -= OnMessagesChanged;
        if (_currentConversation is not null)
            _currentConversation.IsBusyChanged -= OnIsBusyChanged;

        _currentCollection = e.Messages;
        _currentCollection.CollectionChanged += OnMessagesChanged;
        _currentConversation = e;
        _currentConversation.IsBusyChanged += OnIsBusyChanged;

        _autoScroll = true;
        ScrollToBottom();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // 新消息到来：若用户当前已在底部（或正处于流式），继续贴底。
            if (_autoScroll)
                ScrollToBottom();
        }
    }

    private void OnIsBusyChanged(object? sender, bool isBusy)
    {
        // 流式开始（变 busy）时强制回到贴底模式。
        if (isBusy)
        {
            _autoScroll = true;
            ScrollToBottom();
        }
    }

    // 监测用户主动滚动：内容增长时，若用户已在底部则保持贴底；
    // 若用户向上滚动离开底部，则暂停自动贴底，直到其滚回底部。
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var sv = MessagesScrollViewer;

        // 内容高度变化（新文本/新消息）且用户已在底部 -> 维持贴底。
        if (e.ExtentHeightChange > 0 && _autoScroll)
        {
            sv.ScrollToVerticalOffset(sv.ExtentHeight);
            return;
        }

        // 用户主动滚动（非程序触发的内容增长）：根据是否到底更新开关。
        if (e.ExtentHeightChange == 0)
        {
            _autoScroll = sv.VerticalOffset >= sv.ScrollableHeight - 1;
        }
    }

    private void ScrollToBottom()
    {
        MessagesScrollViewer?.ScrollToEnd();
    }
}
