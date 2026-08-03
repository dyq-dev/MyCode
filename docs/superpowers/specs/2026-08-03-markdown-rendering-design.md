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

Markdig.Wpf：`Markdig.Wpf.Markdown.ToFlowDocument(text, pipeline)`，渲染进只读 `RichTextBox`。
样式通过 Markdig.Wpf 的 `Styles.*Key` (ComponentResourceKey) 在应用资源字典中覆盖实现。

> 注：Markdig.Wpf 0.5.0.1（最后发布版，2021-01）没有 Theme 类。默认样式定义在包内
> `Themes/generic.xaml`，通过 `Styles.CodeBlockStyleKey` 等 ComponentResourceKey 暴露。
> 应用级资源字典优先级高于主题资源，可整体覆盖。

## 架构

```
AI.Assistant.Client/
├── Controls/
│   └── MarkdownView.cs      # 新控件：Markdown DP + 200ms 节流渲染（继承只读 RichTextBox）
└── Themes/
    └── Generic.xaml         # 追加 Markdig 样式覆盖（Styles.*Key，对齐蓝色主题）
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

### 样式覆盖（Themes/Generic.xaml）

Markdig.Wpf 无 Theme 类。在 `Themes/Generic.xaml` 中以相同 ComponentResourceKey 覆盖：

- `DocumentStyleKey` → `PrimaryFont`
- `ParagraphStyleKey` → FontSize 14, LineHeight 22, `TextPrimaryBrush`
- `CodeBlockStyleKey` → 背景 `#1E293B`、前景 `#E2E8F0`、`MonoFont`（与 RAG 框一致）
- `CodeStyleKey`（行内代码）→ 背景 `#E2E8F0`、前景 `#1E3A8A`、`MonoFont`
- `Heading1-3StyleKey` → 14-18px SemiBold、`TextPrimaryBrush`（覆盖默认 42px）
- `QuoteBlockStyleKey` → `AccentLightBrush` 左边框、`TextSecondaryBrush`
- `TableStyleKey` / `TableCellStyleKey` / `TableHeaderStyleKey` → `BorderColor` 边框
- `HyperlinkStyleKey` → `AccentBrush` 前景
- `ThematicBreakStyleKey` → `#E2E8F0`

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

- 新增 NuGet：`Markdig.Wpf` 0.5.0.1（最后发布版，依赖 Markdig >= 0.22.0）
- 同时显式固定 `Markdig` 0.38.0（避免 NuGet 解析到 1.x 引入破坏性 API 变更；若运行时报
  TypeLoadException 则降级尝试 0.33.0）

## 测试

- 纯视图层改动，无 Core/Infrastructure 变更
- 验证：`dotnet build AI.Assistant.slnx` + 手动运行验证渲染效果
