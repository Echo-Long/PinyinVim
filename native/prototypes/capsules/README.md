# 胶囊窗口实现

正式产品继续复用此目录的 Neovide GUI 桥、原生窗口策略、六面板 Lua 布局、独立 WPF 设置、快捷键和拥有进程管理。编辑行为与正文仍由 Neovim 负责。

通过 `native/build.ps1` 构建当前版本；通过 `native/package.ps1` 测试并生成发布包。package.json 是唯一产品版本来源。此目录的 build.ps1 支持独立输出和本地视觉对照，package.ps1 转发至正式发布入口。

详情见 [源码构建](../../../docs/build.md)。不要使用历史包作为正式构建的隐式依赖。