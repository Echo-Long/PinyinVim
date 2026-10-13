# 拼音浮笺 {{DISPLAY_VERSION}}

Windows x64 便携版，Neovim + Neovide + 本地中文导航，MIT 开源。

[查看真实界面与使用教程](https://github.com/Echo-Long/PinyinVim/blob/main/README.zh-CN.md) · [完整演示视频 / Full demo](https://github.com/Echo-Long/PinyinVim/blob/main/docs/media/demo.mp4)

本次补丁更新包含 V0.3.0 发布后的基础功能修复，保留毛玻璃、十套主题、背景图片、独立设置和自定义快捷键：

- 按键暂存修复：一个 `2` 不再显示成两个；`f/F/t/T` 等待目标时及时显示，计数、操作符和取消继续保留原有 Vim 语义与中英文查找能力。
- 启动时绑定实际草稿缓冲区，提交前保存草稿；重新唤起清理上一会话残留的 E29 等错误，保留 Vim 消息历史和原生按键含义。
- Ctrl+Enter 按下即接受提交并收起。程序处理本次提交键的长按重复和修饰键状态，长按只提交一次，回填不要求先松键；不向目标补发回车。
- 保留提交去重、严格目标身份和有限焦点等待；复制或回填失败保留草稿。新增模拟按键状态、实际宿主异步提交和 UI grid 回归。
- “系统通知”默认关闭，可在 `F2 → 编辑与草稿 → 系统通知` 中开启或关闭，立即生效并保存。升级保留已有用户明确保存的选择；关闭通知不影响复制、粘贴或草稿保存。
- 双语 README 与便携包说明同步通知默认值及开关入口；保留中英混排、Vim 操作、毛玻璃和外观调整教程及应用图标。
- 默认背景透明度 50%、胶囊透明度 20%、高斯模糊 5px；恢复默认外观同步使用该配置，已有自定义设置保留。
- 程序、关于页面、包名和发布清单继续从同一个版本来源生成；保留 MIT、第三方声明及干净源码构建入口。

使用：解压整个便携包，双击 PinyinVim.exe。Ctrl+Shift+X 呼出，Ctrl+Enter 提交，F2 设置，Normal 下 Esc 收起，托盘菜单退出。

升级前退出旧实例，将新包解压到单独目录。需要保留草稿和设置时，复制旧包 `prototype-profile` 中的 `draft.txt`、`settings.json`、`last-submitted.txt`，不要复制进程记录。详情见双语 README 和便携包说明。

系统需要 .NET Framework 4.8 与 Microsoft Visual C++ v14 x64 运行库。若缺少 VCRUNTIME140.dll，请使用[微软官方运行库](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170)。

我们已测试大量 Windows 应用。已知不可自动回填项包括 Windows 任务栏搜索和微信（复测版本 4.1.13.65），只能使用剪贴板手动粘贴；开启系统通知时会提示结果。发送粘贴按键不代表已经确认目标接收。原剪贴板自动恢复暂缓。真实输入法、多屏 DPI、GPU 视觉和权限差异不由无界面测试证明。

资产：PinyinVim-{{DISPLAY_VERSION}}-win32-x64.zip、PinyinVim-{{DISPLAY_VERSION}}-source.zip、SHA256SUMS.txt。发布包不含用户配置、草稿、日志、进程记录或测试接收端。

English summary: system notifications are **off by default**; toggle them under `F2 → 编辑与草稿 → 系统通知`. Existing saved preferences are preserved. Submission is accepted on key-down and hides the editor immediately; holding the shortcut does not require release before paste-back or cause duplicate submissions. This patch also fixes pending-command feedback, stale session errors and draft startup/persistence. Windows taskbar search and WeChat still require manual clipboard pasting.
