# 拼音浮笺 {{DISPLAY_VERSION}}

将整个文件夹解压到可写目录，双击唯一的 `PinyinVim.exe`。Neovim、Neovide、中文词典和备用字体已经随附，无需安装 Node.js、Python 或 Vim。

系统需要 .NET Framework 4.8 和 Microsoft Visual C++ v14 x64 运行库。若启动提示缺少 `VCRUNTIME140.dll`，安装[微软官方 x64 运行库](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170)，再重新打开。程序不自动安装系统组件。

- Ctrl+Shift+X：呼出或切换浮窗。
- Ctrl+Enter：提交。可确认的输入框会尝试回填一次；其他位置复制到剪贴板。开启“系统通知”时，复制成功发通知，说明是否已发送粘贴。
- Insert、Visual、命令行的 Esc 保持 Vim 原生语义；回到 Normal 后再次 Esc 收起到托盘。
- F2：独立设置。快捷键、主题、背景图片、两类透明度和模糊程度可自定义。
- 外壳空白拖动，四边四角缩放；关闭、Alt+F4 和最小化会收起。真正退出请使用托盘菜单。

提交按钮的悬浮提示会显示“粘贴到原输入框”或“仅复制到剪贴板”。我们已测试大量 Windows 应用；已知无法自动回填的入口包括 Windows 任务栏搜索和微信，只能通过剪贴板手动粘贴。程序不会自动发送聊天消息，也不会因回填失败重复粘贴。

F2 → 编辑与草稿中的“系统通知”默认开启，可以随时关闭。开关立即生效并在重启后保留；关闭通知不影响复制、粘贴及草稿保存。中英混排、Vim 操作和外观调整示例见项目的中英文 README。

默认外观为背景透明度 50%、胶囊透明度 20%、高斯模糊 5px，可在 F2 → 外观调整；“恢复默认外观”也使用这组数值。完整教程见 [GitHub 项目](https://github.com/Echo-Long/PinyinVim)。

配置和草稿位于本目录首次启动创建的 `prototype-profile`。编辑期间自动保存；“退出时清空草稿”仅在托盘退出时生效，Esc 收起保留草稿。该发布包不包含任何用户草稿或配置。

升级：保存需要保留的草稿，从旧版托盘退出，将新版解压到独立目录；需要迁移时只复制旧 `prototype-profile` 中的 settings.json、draft.txt、last-submitted.txt。进程所有权和日志不需要复制。启用了托盘退出清空时，退出会按该设置清空草稿。

本版本为 Windows x64 便携版。真实输入法、多屏 DPI 和其他应用的兼容范围见发布说明。系统禁止应用通知时，Windows 可能不展示通知；文字仍在剪贴板。原剪贴板不会自动恢复。

项目源码采用 MIT。第三方内核、字体、词典和图标保留各自许可，见 LICENSE、THIRD_PARTY_NOTICES.md 和 licenses。
