# PinyinVim {{DISPLAY_VERSION}}

Extract the entire folder to a writable location and open `PinyinVim.exe`. Neovim, Neovide, dictionaries and a fallback font are included. Node.js, Python and a separate Vim installation are not required. The interface is in Simplified Chinese.

The system needs .NET Framework 4.8, Microsoft Visual C++ v14 x64 runtime and a working graphics driver. For a missing `VCRUNTIME140.dll`, use Microsoft's [official x64 runtime download](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170).

- `Ctrl+Shift+X`: show or hide the editor.
- `i`: enter Insert mode from Normal mode and write.
- `Esc`: leave Insert/Visual or cancel the command line; press again in Normal mode to hide.
- `Ctrl+Enter`: submit. A confirmed original input field receives one paste attempt; otherwise, paste the copied text manually.
- `F2`: settings for shortcuts, themes, background images, transparency and blur.
- Close, minimize and `Alt+F4` hide the editor. Use **Exit** in the tray menu to quit.

Default appearance: background transparency **50%**, capsule transparency **20%**, Gaussian blur **5 px**. Under `F2 → 编辑与草稿`, **系统通知** controls system notifications. It is on by default; turning it off leaves copying, pasting and draft saving enabled.

We have tested many Windows applications. **Windows taskbar search and WeChat** require manual clipboard pasting. A “paste sent” notification confirms shortcut dispatch, not receipt. The app does not send Enter to the target, send chat messages or retry pasting. Previous clipboard contents are not restored.

Drafts and settings are saved in `prototype-profile` next to the app. Hiding preserves drafts; “Clear draft on exit” applies only to tray exit. Before upgrading, save needed text, exit the old instance and use a separate new folder. If needed, migrate only `settings.json`, `draft.txt` and `last-submitted.txt`.

Full English and Chinese guides: [PinyinVim on GitHub](https://github.com/Echo-Long/PinyinVim). See `RELEASE_NOTES.md` for this build. Project source is MIT; third-party licenses are retained in `licenses` and `THIRD_PARTY_NOTICES.md`.
