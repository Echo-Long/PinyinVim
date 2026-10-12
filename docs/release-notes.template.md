# 拼音浮笺 {{DISPLAY_VERSION}}

Windows x64 便携版，Neovim + Neovide + 本地中文导航，MIT 开源。

本次发布保留毛玻璃、十套主题、背景图片、独立设置和自定义快捷键，并收敛基础功能与交付流程：

- f/F/t/T 同时保留原生字面字符查找与中文首字母增强；跨行计数、文本对象、空白、寄存器和重复动作增加回归覆盖。
- 草稿自动保存及清空同步磁盘，提交快照校验保护等待期间的新编辑；明确 Esc 收起与托盘退出的区别。
- 关闭自动切换仍支持手工中文输入；Normal/Visual 与 Insert 的输入法策略和设置说明修正。
- 提交去重、严格目标身份与有限焦点等待；Microsoft To Do 用户复测已通过。
- 新增“系统通知”开关，默认开启、立即生效并保存。开启时，每次非空提交成功写入剪贴板均发结果通知，包括非输入框和未识别目标；关闭不影响复制、粘贴或草稿保存。提交按钮提示当前粘贴/复制方式。
- 更新应用图标，程序文件、托盘和编辑窗口共用图标资源；提供双语 README 的中英混排、Vim 操作和外观调整教程。
- 默认背景透明度 50%、胶囊透明度 20%、高斯模糊 5px；恢复默认外观同步使用该配置，已有自定义设置保留。
- 程序、关于页面、包名和发布清单从同一个版本来源生成；补齐 MIT、第三方声明及干净源码构建入口。

使用：解压整个便携包，双击 PinyinVim.exe。Ctrl+Shift+X 呼出，Ctrl+Enter 提交，F2 设置，Normal 下 Esc 收起，托盘菜单退出。

系统需要 .NET Framework 4.8 与 Microsoft Visual C++ v14 x64 运行库。若缺少 VCRUNTIME140.dll，请使用[微软官方运行库](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170)。

我们已测试大量 Windows 应用。已知不可自动回填项包括 Windows 任务栏搜索和微信（复测版本 4.1.13.65），只能使用剪贴板手动粘贴；开启系统通知时会提示结果。发送粘贴按键不代表已经确认目标接收。原剪贴板自动恢复暂缓。真实输入法、多屏 DPI、GPU 视觉和权限差异不由无界面测试证明。

资产：PinyinVim-{{DISPLAY_VERSION}}-win32-x64.zip、PinyinVim-{{DISPLAY_VERSION}}-source.zip、SHA256SUMS.txt。发布包不含用户配置、草稿、日志、进程记录或测试接收端。
