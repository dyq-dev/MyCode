using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Markdig;
using Markdig.Wpf;

namespace AI.Assistant.Client.Controls;

/// <summary>
/// 只读 Markdown 渲染控件。
/// Markdown 变化后 200ms 节流重渲染；IsRenderFinal=true 时立即渲染最终文档。
/// </summary>
public class MarkdownView : RichTextBox
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseSupportedExtensions().Build();

    private readonly DispatcherTimer _renderTimer;

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownView),
        new FrameworkPropertyMetadata(string.Empty, OnMarkdownChanged));

    public static readonly DependencyProperty IsRenderFinalProperty = DependencyProperty.Register(
        nameof(IsRenderFinal), typeof(bool), typeof(MarkdownView),
        new FrameworkPropertyMetadata(true, OnIsRenderFinalChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public bool IsRenderFinal
    {
        get => (bool)GetValue(IsRenderFinalProperty);
        set => SetValue(IsRenderFinalProperty, value);
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

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _renderTimer.Tick += (_, _) => Render();
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        view._renderTimer.Stop();
        view._renderTimer.Start();
    }

    private static void OnIsRenderFinalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        if (view.IsRenderFinal)
        {
            view._renderTimer.Stop();
            view.Render();
        }
    }

    private void Render()
    {
        _renderTimer.Stop();
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
