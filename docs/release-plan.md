# 发布与上传流程

源码已发布到 [Echo-Long/PinyinVim](https://github.com/Echo-Long/PinyinVim)，便携版、源码归档和校验文件从[最新发布版](https://github.com/Echo-Long/PinyinVim/releases/latest)获取。中英文 README 提供真实界面截图、操作教程与演示视频入口。产品版本继续统一取自 `package.json`。

## 发布内容

1. GitHub 仓库：上传 `dist/<显示版本>/source/PinyinVim` 中的源码；清单由 `native/source-files.json` 管理。保留 MIT、第三方许可、构建文档、Windows CI 和问题模板。
2. GitHub Release：上传便携 ZIP、源码 ZIP、SHA256SUMS.txt，正文使用生成的 RELEASE_NOTES.md。Tag 和标题均使用 `package.json` 派生的 `V<版本>`。
3. 本地工作目录保留历史版本、原型、个人草稿与诊断。它们不属于公开源码或成品，不应整目录上传。
4. 展示素材放在 `docs/media`，随公开源码保留。README 内播放使用 GitHub 视频附件，把生成的 `github.com/user-attachments/assets/...` 地址单独放在一段中；仓库 MP4 和 Release 演示附件保留为下载入口。演示附件独立于便携包、源码包和 SHA256SUMS.txt 这三个产品发布文件。

## 发布顺序

先完成构建和测试，再运行打包。打包过程验证程序版本、逐文件哈希、唯一顶层入口、许可证和个人数据排除。再解压两份 ZIP：便携包复验清单；源码包从干净目录获取固定运行时并重新构建。发布前确认没有把草稿、背景图路径、日志、进程所有权记录或个人桌面截图带入上传目录。

在现有 GitHub 仓库维护经过公开源码清单筛选的源码。先推送代码，等待 Windows CI，再为新的软件版本创建与版本元数据一致的 Tag。仓库附带的 tag 工作流在测试通过后创建草稿 Release 并上传资产，最后在 GitHub 核对草稿内容后发布。文档和展示素材更新直接提交到源码仓库；已发布的版本 Tag 和软件归档保留原始内容。

也可手动创建 Release，选择对应 Tag，粘贴 RELEASE_NOTES.md 并上传已校验的三个资产。源码 ZIP 用于归档，源码仓库应上传解压后的文件。

仓库使用 https://github.com/Echo-Long/PinyinVim。我们已测试大量 Windows 应用，Microsoft To Do 回填复测已通过；Windows 任务栏搜索和微信为已知不可自动回填项，使用剪贴板手动粘贴。开启系统通知时会提示复制/粘贴结果。直接输入、微信专用适配、原剪贴板自动恢复保持后续路线。
