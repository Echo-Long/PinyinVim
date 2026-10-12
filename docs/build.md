# 从源码构建

仅构建 Windows x64。需要 Windows PowerShell 5.1 或 PowerShell 7、系统 .NET Framework 编译器，以及 PATH 中的 MinGW-w64 `gcc.exe`。Node.js 20 或更新版本可用于下载运行时和重新生成词典；正常运行便携包不需要 Node.js。

版本唯一来源是根目录 `package.json` 的 `version`。修改此字段后，构建自动生成程序文件版本、产品版本、“关于”页版本以及发布目录/文件名。Neovim、Neovide 的固定来源和 SHA-256 位于 `native/runtime-assets.json`。

```powershell
native\setup-runtime.ps1
native\build.ps1
native\package.ps1 -UseExistingBuild
```

打包入口自动运行完整检查。实际构建路径由 `native/release-common.ps1` 的 `Get-PinyinRelease` 获取，无需把版本复制进其他脚本。已有 Node.js 时，也可使用 `npm run build`、`npm test`、`npm run package:win`、`npm start`。日常构建不需要安装 npm 依赖。

运行时第一次通过上游官方 HTTPS 地址下载，并核对整个 ZIP 的 SHA-256；安装后再次核对可执行文件。已存在且匹配的运行时会直接复用。下载失败或校验不匹配就停止，不使用其他版本、不跳过校验、不覆盖已有的不匹配运行时。需要离线构建时，可把经过校验的两个 ZIP 放到 `setup-runtime.ps1 -CacheDirectory <目录>` 指定的缓存目录，文件名分别按 `<组件>-<版本>.zip` 命名。

正式构建直接编译当前 C# 宿主、GUI 桥和原生窗口策略，加载当前 Neovim 配置与胶囊 Lua；无需历史 `dist/0.2.*` 目录。`native/prototypes/capsules/build.ps1 -PresentationBaseline` 仅供本地视觉对照，正式打包入口不使用该选项。

产物位于 `dist/<显示版本>/`：build 为测试构建，PinyinVim-win32-x64 为无个人配置的便携目录，source/PinyinVim 为源码上传目录，两个 ZIP 分别用于 Release 与源码归档。输出目录运行中时拒绝构建/打包；正式便携目录已存在时拒绝覆盖。

词典已经包含在源码中。仅修改词典来源时运行 `npm ci` 和 `npm run data:regenerate`。

应用图标位于 `assets/branding/pinyinvim.ico`，构建将同一文件嵌入程序文件图标和宿主资源，托盘与编辑窗口复用该资源。更换高清 PNG 后，可运行 `assets/branding/export-icon.ps1` 导出多尺寸 ICO；正常构建和运行不需要图像生成工具。胶囊内的齿轮/返回图标是独立的字体，重建使用 `native/prototypes/capsules/make-icons.py`，需要已有 Python 和 fontTools；正常构建使用现有图标字体。

测试区分无界面合同与真实桌面验收。默认测试不操作用户窗口、不写入系统剪贴板、不发送真实键鼠；它们不能代替系统输入法、多屏 DPI、GPU 视觉和外部应用接收文字的实际验收。
