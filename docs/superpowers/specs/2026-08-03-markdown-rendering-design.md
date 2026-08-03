# Markdown 渲染设计

日期: 2026-08-03
状态: 已批准

## 目标

为 AI 助手消息的文本输出增加 Markdown 渲染。用户消息、系统消息、RAG 详情框保持纯文本。

## 功能范围

- 基础元素：标题、加粗/斜体、列表、引用、行内代码、链接
- 代码块：深色背景 + 等宽字体
- 表格
- 不做语法高亮、不做代码块复制按钮、不做"复制原文"菜单

## 流式行为

- 打字机逐字追加期间，`MarkdownView` 以 200ms 节流重渲染（DispatcherTimer 防抖）
- 流结束时立即渲染最终文档，避免格式跳动的延迟感

## 方案

Markdig.Wpf：`Markdown.ToFlowDocument(text, pipeline, theme)`，渲染进只读 `RichTextBox`。

## 架构

```
AI.Assistant.Client/
└── Controls/
    └── MarkdownView.cs      # 新控件：Markdown DP + 节流渲染
    └── AppMarkdownTheme.cs  # 继承 Markdig.Wpf.Themes.Theme，贴合蓝色主题
```

### MarkdownView（继承 RichTextBox）

- 只读、无边框、透明背景、不可聚焦
- 依赖属性：
  - `Markdown` (string)：绑定 `ChatMessageViewModel.Content`
  - `IsRenderFinal` (bool)：true 时取消节流立即渲染
- 内部 `DispatcherTimer`（200ms）：`Markdown` 变化时重置定时器，到点渲染
- 渲染逻辑：
  - pipeline 使用 Markdig.Wpf 支持的扩展集（含表格）
  - `IsRenderFinal=true` → 停止定时器 + 立即渲染
  - 空内容 → 空文档
  - `ToFlowDocument` 抛异常 → 回退为纯文本段落

### AppMarkdownTheme

- 继承 `Markdig.Wpf.Themes.Theme`
- 代码块背景沿用 RAG 框的 `#1E293B`，代码前景 `#E2E8F0`
- 引用、链接、表格颜色匹配现有蓝色主题（AccentBrush）

### ChatMessageViewModel 变更

新增 `IsRenderFinal` (bool, 默认 true)：

- 历史消息加载：默认 true，直接渲染
- 新流式消息创建：置 false
- 流结束（`WaitForTypewriterAsync` 之后 / 取消 flush / 出错 flush 路径）：置 true

### XAML 变更

- MainWindow.xaml 助手气泡内容 TextBlock（第 555-560 行）替换为：

```xml
<controls:MarkdownView Markdown="{Binding Content}"
                       IsRenderFinal="{Binding IsRenderFinal}" />
```

- 用户消息、系统消息、RAG 上下文框保持不变

## 数据流

1. 打字机逐字追加 → `Content` 属性变化 → `Markdown` DP 变化
2. 200ms 防抖 → `ToFlowDocument` → 替换 `Document`
3. 文档高度变化 → 现有贴底逻辑（MainWindow.xaml.cs `OnScrollChanged`）自动跟随

## 错误处理

- `ToFlowDocument` 包 try/catch，异常时回退为纯文本段落
- 空/空白内容渲染空文档

## 复制行为

RichTextBox 原生文本选中复制（复制渲染后的文本）。不额外提供"复制原文"。

## 依赖

- 新增 NuGet：`Markdig.Wpf`（含 Markdig 解析器）

## 测试

- 纯视图层改动，无 Core/Infrastructure 变更
- 验证：`dotnet build AI.Assistant.slnx` + 手动运行验证渲染效果
