# UI Redesign — Fresh Teal Theme

## Overview

Refresh the WPF chat client UI to match the user's preference for "简约、清新、不突兀" (minimalist, fresh, unobtrusive). Flat Design style with teal color system. Sidebar becomes collapsible. No backend changes — only XAML styles, resource dictionary, and layout toggles.

## Color System

| Role | Current | New |
|------|---------|-----|
| Primary | `#10A37F` | `#0D9488` |
| On Primary | `#FFFFFF` | `#FFFFFF` |
| Secondary | `#14B8A6` | (kept as accent variant) |
| Background | `#FFFFFF` | `#F0FDFA` |
| Surface | `#F9FAFB` | `#FFFFFF` |
| Sidebar | `#F3F4F6` | `#FFFFFF` |
| Muted | `#F3F4F6` | `#E8F1F4` |
| Border | `#E5E7EB` | `#CCFBF1` |
| Text Primary | `#111827` | `#134E4A` |
| Text Secondary | `#6B7280` | `#5C7A6F` |
| Text Tertiary | `#9CA3AF` | `#94A3B8` |
| User Bubble | `#111827` black | `#0D9488` teal |
| User Bubble Text | `#FFFFFF` | `#FFFFFF` |
| Assistant Bubble | `#F3F4F6` gray | `#FFFFFF` white |
| Assistant Bubble Text | `#111827` | `#134E4A` |
| Divider | `#F3F4F6` | `#F0FDFA` |
| Destructive | `#EF4444` | `#DC2626` |

## Layout

| Aspect | Detail |
|--------|--------|
| Structure | Left sidebar (expand/collapse) + right chat area |
| Expanded width | 240px (current) |
| Collapsed width | 48px — narrow strip, icons only |
| Toggle button | Top-right of sidebar, next to logo |
| Collapsed state | Logo icon only, new-chat icon (no text), knowledge source list hidden, playground hidden, RAG toggle hidden |

## Component Styles

### Chat Bubbles

| Property | User | Assistant |
|----------|------|-----------|
| Background | `#0D9488` | `#FFFFFF` |
| Foreground | `#FFFFFF` | `#134E4A` |
| Border | none | `#CCFBF1` 1px |
| CornerRadius | 16px (top-right 4px) | 16px (top-left 4px) |
| Alignment | Right | Left |
| Max width | 75% | 85% |

### Input Area

- White background, rounded 12px border, teal border `#CCFBF1`
- Focus: teal border `#0D9488`
- Send button: circular, teal background, white arrow icon

### Sidebar Items

- Hover: `#F0FDFA` background
- Selected: `#CCFBF1` background, `#0D9488` text
- Normal: transparent, `#5C7A6F` text

### Buttons

- New Chat: white card, teal border on hover, subtle shadow
- Delete: hidden by default, show on hover (red tint)
- Generic text buttons: transparent, teal text on hover

## Typography

| Token | Value |
|-------|-------|
| PrimaryFont | `Segoe UI Variable, Segoe UI, Microsoft YaHei UI, sans-serif` |
| DisplayFont | `Segoe UI Variable Display, Segoe UI, sans-serif` |
| MonoFont | `Cascadia Code, JetBrains Mono, Consolas, monospace` |

(No font changes — system fonts only.)

## Effects

| Token | Value |
|-------|-------|
| SmallRadius | 6px |
| MediumRadius | 12px |
| LargeRadius | 16px |
| XLargeRadius | 20px |
| FullRadius | 999px |
| SmallShadow | Blur 8, Depth 1, Opacity 0.06 |
| MediumShadow | Blur 16, Depth 2, Opacity 0.08 |

## Icon Migration

Replace all emoji-based icons with Path vector icons:

| Location | Current | Replace with |
|----------|---------|-------------|
| Knowledge source icon | `🔍` emoji | Search Path icon |
| Playground button | `🔍` emoji | Search Path icon |
| Knowledge source status | text badge | (keep text badge) |

## File Boundaries & Risk

```
Generic.xaml (global palette)
├── Color / SolidColorBrush tokens     ← SAFE: named resources, no implicit override
├── Named Styles (x:Key)               ← SAFE: only applied via explicit {StaticResource}
│   ├── NewChatButton
│   ├── ConversationItemButton
│   ├── DeleteButton
│   ├── SendButton
│   └── InputTextBox
└── [NEW] BubbleContainerStyle          ← NEW named style, SAFE
└── [NEW] SidebarToggleStyle            ← NEW named style, SAFE
```

**All styles use `x:Key` — no implicit type-targeting.** No Button/TextBox/ScrollViewer will be globally recolored.

## Files to Modify

| File | What Changes |
|------|-------------|
| `Themes/Generic.xaml` | Update color token values; add new named styles (BubbleContainer, SidebarToggle) |
| `Views/MainWindow.xaml` | Sidebar collapsible layout; emoji→Path icons; bubble style references; Grid animation triggers |

No new files. No backend/ViewModel changes.

## Animations

Implementation in pure XAML Storyboards — no code-behind.

| Element | Animation | Trigger | Timing |
|---------|-----------|---------|--------|
| Sidebar collapse/expand | `DoubleAnimation` on `ColumnDefinition.Width` | ToggleButton IsChecked | 300ms |
| Sidebar content fade | `DoubleAnimation` on content Opacity | ToggleButton IsChecked | 200ms |
| Button background | `ColorAnimation` on hover/press | Trigger IsMouseOver/IsPressed | 150ms |
| Delete button | `ObjectAnimationUsingKeyFrames` Visibility | Border IsMouseOver | 100ms |

No bubble appearance animation (XAML-only ItemsControl item animation is fragile).

## Non-goals

- No dark mode (potential future feature)
- No font downloads (system fonts only)
- No external animation library
- No NuGet dependency changes
