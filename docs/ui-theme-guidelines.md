# DML 界面主题与字体颜色规范

本文档用于提醒后续界面开发：所有文字和控件状态都必须同时适配浅色、深色及跟随系统主题。新增或修改 XAML 时，应主动检查颜色来源，不能依赖 WPF 系统控件的默认前景色。

## 为什么必须显式设置主题颜色

WPF 的 `Foreground` 通常可以沿可视树继承，但以下情况很容易中断继承或重新使用系统默认颜色：

- `ListBox`、`ListBoxItem`、`ComboBoxItem` 等系统控件拥有自己的主题样式。
- `DataTemplate`、`ControlTemplate`、`Popup` 和独立 `Window` 可能不继承主窗口的前景色。
- 对话框、弹出菜单和列表项在浅色主题下看似正常，但在深色主题下可能仍显示固定黑色。
- 系统默认样式会随着 Windows 主题变化，不一定与 DML 当前选择的主题一致。

因此，只要文字位于上述边界中，或其可读性对功能有影响，就应显式绑定 DML 的主题资源。

## 应使用的颜色资源

普通主要文字：

```xml
Foreground="{DynamicResource PageForeground}"
```

说明、次要信息：

```xml
Foreground="{DynamicResource MutedForeground}"
```

更弱的辅助信息、路径和提示：

```xml
Foreground="{DynamicResource SubtleForegroundBrush}"
```

强调信息：

```xml
Foreground="{DynamicResource AccentBrush}"
```

强调色按钮上的文字：

```xml
Foreground="{DynamicResource AccentForegroundBrush}"
```

日志专用文字：

```xml
Foreground="{DynamicResource LogForegroundBrush}"
```

除非颜色是品牌元素或明确不随主题变化，否则不要直接写 `Black`、`White` 或十六进制字体颜色。

## 必须重点检查的控件

新增或修改以下区域时，必须分别在浅色和深色主题下检查：

- `Window`、自定义对话框及其标题、标签。
- `ListBox`、`ListView`、`ItemsControl` 的项目模板。
- `ComboBox`、`ComboBoxItem` 和 `Popup` 内的文字。
- `DataTemplate` 与 `ControlTemplate` 中的 `TextBlock`、`ContentPresenter`。
- 按钮的正常、悬停、按下、禁用状态。
- 文本框的文字、提示文字、选区和禁用状态。
- 空状态、错误提示、磁盘警告和状态栏信息。

实例列表曾出现过实例名称在深色模式中仍为黑色的问题；实例创建/重命名对话框也曾因独立窗口未继承主窗口前景色而出现同样问题。此类模板和独立窗口不得依赖默认颜色。

## 推荐写法

列表项中的主要名称应显式设置：

```xml
<TextBlock Text="{Binding Name}"
           Foreground="{DynamicResource PageForeground}" />
```

独立对话框中的标题和字段标签应显式设置：

```xml
<TextBlock Text="创建实例"
           Foreground="{DynamicResource PageForeground}" />
```

如果一个容器内所有子元素都应使用同一种前景色，可以在容器或控件样式上统一设置；仍需确认内部系统模板没有覆盖该值。

必须使用 `DynamicResource`，不要改用 `StaticResource`。DML 支持运行时切换主题，动态资源才能在主题变化后更新现有界面。

## 提交前检查清单

- 在浅色主题下打开本次修改涉及的所有页面和对话框。
- 在深色主题下重复检查，确认主要文字有足够对比度。
- 切换主题后不重启应用，确认已显示的控件能够立即更新颜色。
- 检查列表选中、悬停、禁用和空状态，而不只检查默认状态。
- 检查弹出层和独立窗口，它们最容易失去主窗口的颜色继承。
- 搜索新增 XAML 中的固定字体颜色，并确认每一处都是有意设计。

可使用以下命令辅助检查固定颜色，但最终仍需人工查看界面：

```powershell
rg -n 'Foreground="(Black|White|#[0-9A-Fa-f]{6,8})"' src/Dml.Launcher.App
```

