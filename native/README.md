# 原生产品源码

当前正式路线为 Neovim + Neovide + Windows 宿主。构建入口是 `build.ps1`，测试入口是 `test.ps1`，打包入口是 `package.ps1`，启动入口是 `start.ps1`。它们共用根目录 package.json 的版本信息。

`prototypes/capsules` 保留成熟的窗口策略、GUI 桥、Lua 布局和独立设置实现，正式构建继续复用这些源码。旧目录名称不代表当前发布仍是早期 Phase 1。

具体使用与构建见 [项目说明](../README.md)、[构建文档](../docs/build.md)和[发布流程](../docs/release-plan.md)。