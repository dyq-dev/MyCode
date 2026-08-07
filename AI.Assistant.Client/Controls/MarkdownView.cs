using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Markdig;
using Markdig.Wpf;

namespace AI.Assistant.Client.Controls;

/// <summary>
/// 只读 Markdown 渲染控件（方案 A）。
/// 流式期间（IsStreaming=true）显示纯文本，零卡顿；
/// 流式结束后（IsStreaming=false）渲染完整 Markdown。
/// </summary>
public class MarkdownView : RichTextBox
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseSupportedExtensions().Build();

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownView),
        new FrameworkPropertyMetadata(string.Empty, OnMarkdownChanged));

    public static readonly DependencyProperty IsStreamingProperty = DependencyProperty.Register(
        nameof(IsStreaming), typeof(bool), typeof(MarkdownView),
        new FrameworkPropertyMetadata(false, OnIsStreamingChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public MarkdownView()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Focusable = false;
        Background = System.Windows.Media.Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        IsDocumentEnabled = true;
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        if (!view.IsStreaming)
            view.RenderMarkdown();
        else
            view.RenderPlainText();
    }

    private static void OnIsStreamingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        if (view.IsStreaming)
            view.RenderPlainText();
        else
            view.RenderMarkdown();
    }

    private void RenderPlainText()
    {
        var md = Markdown ?? string.Empty;
        Document = new FlowDocument(new Paragraph(new Run(md)));
    }

    private void RenderMarkdown()
    {
        var md = Markdown ?? string.Empty;
        try
        {
            Document = Markdig.Wpf.Markdown.ToFlowDocument(md, Pipeline);
        }
        catch
        {
            Document = new FlowDocument(new Paragraph(new Run(md)));
        }
    }
}
