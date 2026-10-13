# 第三方声明

PinyinVim 自有源码采用 MIT，见 LICENSE。以下组件和数据保留上游许可；项目许可证不替代它们。

| 组件 | 固定来源/用途 | 许可文件 |
| --- | --- | --- |
| Neovim | neovim/neovim v0.12.5，编辑内核及官方运行时 | native/vendor/neovim-APACHE-2.0.txt；发布包同时保留 Vim runtime 许可 |
| Neovide | neovide/neovide 0.16.2，成熟 GUI | native/vendor/neovide-LICENSE.txt |
| jieba | https://github.com/fxsjy/jieba，本地词典来源 | native/vendor/jieba-LICENSE |
| pinyin-pro | https://github.com/zh-lx/pinyin-pro，3.29.4，拼音/多音字及词典生成 | native/vendor/pinyin-pro-LICENSE.txt |
| Sarasa Mono SC | https://github.com/be5invis/Sarasa-Gothic，v1.0.42，中文备用字体 | app/fonts/OFL-1.1.txt |
| PinyinVim Icons | 本项目原创齿轮与返回图标；仅两个码点 | native/prototypes/capsules/assets/ICONS-LICENSE.txt，CC0 1.0 |

运行时下载地址及 ZIP/可执行文件 SHA-256 位于 native/runtime-assets.json。更纱黑体来源、校验值见 app/fonts/README.md。Consolas 和 Microsoft YaHei UI 只使用系统已有字体，不随包再分发。

源码中的 native/vendor 保留数据和许可；便携包的 licenses 目录提供完整声明。旧 Electron/CodeMirror 路线不属于当前运行或构建依赖。