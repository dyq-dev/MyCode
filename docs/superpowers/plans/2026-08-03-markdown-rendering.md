# Markdown 渲染实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 AI 助手消息的文本输出增加 Markdown 渲染（基础元素 + 代码块 + 表格），流式输出期间 200ms 节流重渲染。

**Architecture:** 新增 `MarkdownView` 控件（继承只读 `RichTextBox`），`Markdown`/`IsRenderFinal` 两个依赖属性驱动 200ms DispatcherTimer 防抖渲染；用 `Markdig.Wpf.Markdown.ToFlowDocument(text, pipeline)` 转 FlowDocument。样式在 `Themes/Generic.xaml` 中以 `Styles.*Key` ComponentResourceKey 覆盖。

**Tech Stack:** WPF (.NET 8, net8.0-windows), CommunityToolkit.Mvvm 8.4.0, Markdig.Wpf 0.5.0.1, Markdig 0.38.0

## Global Constraints

- 仅 AI 助手消息渲染 Markdown；用户消息、系统消息、RAG 详情框保持纯文本
- 不做语法高亮、不做代码块复制按钮、不做"复制原文"菜单
- 保留打字机逐字输出效果；打字机期间 200ms 节流重渲染，流结束立即最终渲染
- `ChatMessageViewModel.Content` 始终保存 Markdown 原文（持久化、复制、历史加载依赖它）
- 新依赖必须显式固定：`Markdig.Wpf` 0.5.0.1 + `Markdig` 0.38.0（不可解析到 Markdig 1.x）
- Demo 模式（无 IChatService）路径必须保持可用
- 构建验证：`dotnet build AI.Assistant.slnx`（必须零错误）

---

### Task 1: 添加 Markdig 依赖并验证构建

**Files:**
- Modify: `AI.Assistant.Client/AI.Assistant.Client.csproj`

**Interfaces:**
- Consumes: 无
- Produces: `Markdig.Wpf` 程序集（Task 2、3 使用）

- [ ] **Step 1: 添加包引用**

在 `AI.Assistant.Client/AI.Assistant.Client.csproj` 的 `<ItemGroup>` 中加入：

```xml
<PackageReference Include="Markdig" Version="0.38.0" />
<PackageReference Include="Markdig.Wpf" Version="0.5.0.1" />
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build AI.Assistant.slnx`
Expected: Build succeeded, 0 Error

- [ ] **Step 3: 兼容性冒烟检查**

Run: `dotnet run --project AI.Assistant.Client` 启动应用，确认主窗口正常打开无崩溃。
如果启动时报 `TypeLoadException`（Markdig 0.38 与 Markdig.Wpf 0.5.0.1 API 不兼容），把 csproj 中 `Markdig` 版本降为 `0.33.0` 再构建运行。

- [ ] **Step 4: 提交**

```bash
git add AI.Assistant.Client/AI.Assistant.Client.csproj
git commit -m "build(chat): add Markdig.Wpf 0.5.0.1 + Markdig 0.38.0 for markdown rendering"
```

---

### Task 2: 实现 MarkdownView 控件

**Files:**
- Create: `AI.Assistant.Client/Controls/MarkdownView.cs`

**Interfaces:**
- Consumes: Task 1 的 `Markdig.Wpf` / `Markdig` 程序集
- Produces:
  - 类 `AI.Assistant.Client.Controls.MarkdownView : RichTextBox`
  - DP `Markdown` (string, 默认 `string.Empty`)
  - DP `IsRenderFinal` (bool, 默认 `true`)
  - 行为：`Markdown` 变化 → 重启 200ms 防抖定时器；`IsRenderFinal=true` → 停定时器立即渲染

- [ ] **Step 1: 创建控件文件**

创建 `AI.Assistant.Client/Controls/MarkdownView.cs`（目录不存在则创建）：

```csharp
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
```

注意：`Markdig.Wpf.Markdown.ToFlowDocument` 必须用全限定名，因为同时 using 了 `Markdig` 和 `Markdig.Wpf` 两个命名空间（都有 `Markdown` 类，裸名会歧义）。

- [ ] **Step 2: 构建验证**

Run: `dotnet build AI.Assistant.slnx`
Expected: Build succeeded, 0 Error

- [ ] **Step 3: 提交**

```bash
git add AI.Assistant.Client/Controls/MarkdownView.cs
git commit -m "feat(chat): add MarkdownView control with 200ms throttled rendering"
```

---

### Task 3: 覆盖 Markdig 默认样式（Themes/Generic.xaml）

**Files:**
- Modify: `AI.Assistant.Client/Themes/Generic.xaml`（文件末尾 `</ResourceDictionary>` 前追加）

**Interfaces:**
- Consumes: Task 1 的 `Markdig.Wpf.Styles` 静态类（ComponentResourceKey）
- Produces: 覆盖后的 Markdig 样式资源（Task 4 的 XAML 替换后生效）

- [ ] **Step 1: 添加 markdig 命名空间**

`Themes/Generic.xaml` 根元素改为：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:markdig="clr-namespace:Markdig.Wpf;assembly=Markdig.Wpf">
```

- [ ] **Step 2: 追加样式覆盖**

在 `</ResourceDictionary>` 之前追加（颜色对齐当前蓝色主题，代码块沿用 RAG 框的深色）：

```xml
    <!-- ============ Markdown 渲染样式（覆盖 Markdig.Wpf 默认样式） ============ -->
    <Style TargetType="FlowDocument" x:Key="{x:Static markdig:Styles.DocumentStyleKey}">
        <Setter Property="FontFamily" Value="{StaticResource PrimaryFont}" />
        <Setter Property="TextAlignment" Value="Left" />
    </Style>
    <Style TargetType="Paragraph" x:Key="{x:Static markdig:Styles.ParagraphStyleKey}">
        <Setter Property="FontSize" Value="14" />
        <Setter Property="LineHeight" Value="22" />
        <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
        <Setter Property="Margin" Value="0,4" />
    </Style>
    <Style TargetType="Paragraph" x:Key="{x:Static markdig:Styles.CodeBlockStyleKey}">
        <Setter Property="Background" Value="#1E293B" />
        <Setter Property="Foreground" Value="#E2E8F0" />
        <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
        <Setter Property="FontSize" Value="12.5" />
        <Setter Property="Padding" Value="12,8" />
        <Setter Property="Margin" Value="0,8" />
        <Setter Property="BorderBrush" Value="#334155" />
        <Setter Property="BorderThickness" Value="1" />
    </Style>
    <Style TargetType="Run" x:Key="{x:Static markdig:Styles.CodeStyleKey}">
        <Setter Property="Background" Value="#E2E8F0" />
        <Setter Property="Foreground" Value="#1E3A8A" />
        <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
        <Setter Property="FontSize" Value="12.5" />
    </Style>
    <Style TargetType="Paragraph" x:Key="{x:Static markdig:Styles.Heading1StyleKey}">
        <Setter Property="FontSize" Value="18" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
        <Setter Property="Margin" Value="0,10,0,4" />
    </Style>
    <Style TargetType="Paragraph" x:Key="{x:Static markdig:Styles.Heading2StyleKey}">
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
        <Setter Property="Margin" Value="0,8,0,4" />
    </Style>
    <Style TargetType="Paragraph" x:Key="{x:Static markdig:Styles.Heading3StyleKey}">
        <Setter Property="FontSize" Value="15" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
        <Setter Property="Margin" Value="0,8,0,4" />
    </Style>
    <Style TargetType="Paragraph" x:Key="{x:Static markdig:Styles.Heading4StyleKey}">
        <Setter Property="FontSize" Value="14" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
        <Setter Property="Margin" Value="0,6,0,4" />
    </Style>
    <Style TargetType="Paragraph" x:Key="{x:Static markdig:Styles.Heading5StyleKey}">
        <Setter Property="FontSize" Value="13.5" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}" />
        <Setter Property="Margin" Value="0,6,0,4" />
    </Style>
    <Style TargetType="Paragraph" x:Key="{x:Static markdig:Styles.Heading6StyleKey}">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}" />
        <Setter Property="Margin" Value="0,6,0,4" />
    </Style>
    <Style TargetType="Section" x:Key="{x:Static markdig:Styles.QuoteBlockStyleKey}">
        <Setter Property="BorderBrush" Value="{StaticResource AccentLightBrush}" />
        <Setter Property="BorderThickness" Value="3,0,0,0" />
        <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}" />
        <Setter Property="Padding" Value="12,2,0,2" />
    </Style>
    <Style TargetType="Table" x:Key="{x:Static markdig:Styles.TableStyleKey}">
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}" />
        <Setter Property="BorderThickness" Value="0,0,1,1" />
        <Setter Property="CellSpacing" Value="0" />
        <Setter Property="Margin" Value="0,8" />
    </Style>
    <Style TargetType="TableCell" x:Key="{x:Static markdig:Styles.TableCellStyleKey}">
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}" />
        <Setter Property="BorderThickness" Value="1,1,0,0" />
        <Setter Property="Padding" Value="6,4" />
    </Style>
    <Style TargetType="TableRow" x:Key="{x:Static markdig:Styles.TableHeaderStyleKey}">
        <Setter Property="FontWeight" Value="Bold" />
        <Setter Property="Background" Value="#F1F5F9" />
    </Style>
    <Style TargetType="Hyperlink" x:Key="{x:Static markdig:Styles.HyperlinkStyleKey}">
        <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
        <Setter Property="Cursor" Value="Hand" />
    </Style>
    <Style TargetType="Line" x:Key="{x:Static markdig:Styles.ThematicBreakStyleKey}">
        <Setter Property="Stretch" Value="Fill" />
        <Setter Property="Stroke" Value="#E2E8F0" />
        <Setter Property="StrokeThickness" Value="1.5" />
    </Style>
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build AI.Assistant.slnx`
Expected: Build succeeded, 0 Error（XAML 编译错误会在此暴露）

- [ ] **Step 4: 提交**

```bash
git add AI.Assistant.Client/Themes/Generic.xaml
git commit -m "style(chat): override Markdig.Wpf default styles to match blue theme"
```

---

### Task 4: ChatMessageViewModel 增加 IsRenderFinal + ConversationViewModel 流式收尾

**Files:**
- Modify: `AI.Assistant.Client/ViewModels/ConversationViewModel.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `ChatMessageViewModel.IsRenderFinal` (bool, 默认 true)
  - 语义：流式消息创建时 false；内容最终确定后置 true（Task 5 的 XAML 绑定它）

- [ ] **Step 1: ChatMessageViewModel 新增属性**

在 `ChatMessageViewModel` 类中（`_ragContextText` 属性之后）添加：

```csharp
    /// <summary>内容是否已最终确定（false 表示流式打字机仍在输出）</summary>
    [ObservableProperty]
    private bool _isRenderFinal = true;
```

- [ ] **Step 2: 真实 AI 路径 — 创建消息时置 false**

`SendMessageAsync` 中真实 AI 分支（约 296-301 行）：

```csharp
        var assistantMessage = new ChatMessageViewModel
        {
            Content = string.Empty,
            Role = MessageRole.Assistant,
            Timestamp = DateTime.Now
        };
```
之后追加一行：
```csharp
        assistantMessage.IsRenderFinal = false;
```

- [ ] **Step 3: 真实 AI 路径 — 流式结束后置 true**

`await WaitForTypewriterAsync(_streamCts.Token);` 之后追加：

```csharp
            assistantMessage.IsRenderFinal = true;
```

- [ ] **Step 4: 取消路径**

`catch (OperationCanceledException)` 块中，`FlushTypewriter();` 之后、`if (assistantMessage.Content.Length == 0)` 之前追加：

```csharp
            assistantMessage.IsRenderFinal = true;
```

- [ ] **Step 5: 出错路径**

`catch (Exception ex)` 块中，`assistantMessage.Role = MessageRole.System;` 之后追加：

```csharp
            assistantMessage.IsRenderFinal = true;
```

- [ ] **Step 6: Demo 路径**

`SendMessageAsync` Demo 分支（约 277-282 行），`StartWaiting(demoMsg);` 之后追加：

```csharp
            demoMsg.IsRenderFinal = false;
```

（Demo 无显式收尾点，200ms 防抖会在打字机完成后渲染最终文档。）

- [ ] **Step 7: 构建验证**

Run: `dotnet build AI.Assistant.slnx`
Expected: Build succeeded, 0 Error

- [ ] **Step 8: 提交**

```bash
git add AI.Assistant.Client/ViewModels/ConversationViewModel.cs
git commit -m "feat(chat): track IsRenderFinal on messages for immediate final markdown render"
```

---

### Task 5: XAML 替换助手消息内容为 MarkdownView

**Files:**
- Modify: `AI.Assistant.Client/Views/MainWindow.xaml`

**Interfaces:**
- Consumes:
  - `AI.Assistant.Client.Controls.MarkdownView`（Task 2）
  - `ChatMessageViewModel.Content` / `IsRenderFinal`（Task 4）
- Produces: 助手气泡 Markdown 渲染生效

- [ ] **Step 1: 添加 controls 命名空间**

`<Window ...>` 根元素（第 6 行 `xmlns:views=...` 附近）添加：

```xml
        xmlns:controls="clr-namespace:AI.Assistant.Client.Controls"
```

- [ ] **Step 2: 替换助手内容 TextBlock**

`MainWindow.xaml` 第 555-560 行（助手气泡 Grid.Row="2" 的 TextBlock）：

```xml
                                                <TextBlock Grid.Row="2"
                                                           Text="{Binding Content}"
                                                           TextWrapping="Wrap"
                                                           FontSize="14"
                                                           LineHeight="22"
                                                           Foreground="{StaticResource TextPrimaryBrush}" />
```

替换为：

```xml
                                                <controls:MarkdownView Grid.Row="2"
                                                                       Markdown="{Binding Content}"
                                                                       IsRenderFinal="{Binding IsRenderFinal}" />
```

注意：`TextWrapping`/`FontSize`/`Foreground` 不再需要——FlowDocument 自动换行，字体由 Task 3 的 `DocumentStyleKey`/`ParagraphStyleKey` 覆盖提供。

- [ ] **Step 3: 构建验证**

Run: `dotnet build AI.Assistant.slnx`
Expected: Build succeeded, 0 Error

- [ ] **Step 4: 提交**

```bash
git add AI.Assistant.Client/Views/MainWindow.xaml
git commit -m "feat(chat): render assistant messages with MarkdownView"
```

---

### Task 6: 端到端手工验证

**Files:** 无代码改动

**Interfaces:** 无

- [ ] **Step 1: 启动应用**

Run: `dotnet run --project AI.Assistant.Client`

- [ ] **Step 2: 功能验证清单**

用一条包含以下内容的消息验证（真实 AI 或 Demo 模式均可）：

```
# 标题
## 二级标题

普通段落，支持 **加粗**、*斜体*、`行内代码` 和 [链接](https://example.com)。

> 引用内容

1. 有序列表
2. 第二项

- 无序项目
- 另一项

| 列A | 列B |
|-----|-----|
| 1   | 2   |

```csharp
public void Hello() { }
```
```

逐项确认：
1. 标题、加粗/斜体、行内代码、链接、引用、列表、表格、代码块均正确渲染
2. 代码块为深色背景（#1E293B）+ 等宽字体，与 RAG 框风格一致
3. 流式期间格式约每 200ms 刷新一次，无逐字重排卡顿
4. 流结束后立即出现最终渲染（无 200ms 延迟感）
5. 用户消息仍是纯文本气泡
6. 消息可选中复制；切到其他对话再切回，历史消息直接渲染成 Markdown
7. 发送时快速点击"停止"（若可用），确认取消路径不崩溃、渲染正常收尾

- [ ] **Step 3: 回归验证**

Run: `dotnet test AI.Assistant.Tests`
Expected: 全部通过（本特性不涉及 Core/Infrastructure，仅确认无回归）

- [ ] **Step 4: 提交（如验证中发现修复项）**

若验证发现问题，修复后单独提交（见各 Task 的提交命令风格）；全部通过则无需提交。

---

## Self-Review 记录

- **规格覆盖**: 基础元素(T3 样式+T2 渲染)、代码块(T3 CodeBlockStyle)、表格(T3 Table* + UseSupportedExtensions 含 PipeTables/GridTables)、流式节流(T2 200ms 定时器)、IsRenderFinal(T4)、仅 AI 消息(T5 只换助手气泡)、错误回退(T2 catch 纯文本)、无复制原文(T1-T6 均未涉及) — 全部覆盖
- **占位符检查**: 无 TBD/TODO，所有代码块完整
- **类型一致性**: `MarkdownView.Markdown`/`IsRenderFinal`、`ChatMessageViewModel.IsRenderFinal`、`Styles.*Key` 名称在 Task 2-5 中一致；`Markdig.Wpf.Markdown.ToFlowDocument` 全限定名已统一
