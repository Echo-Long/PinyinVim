# PinyinVim application icon

The application mark combines a floating frosted note, a broad V-shaped editing stroke and a mint insertion cursor on a blue-to-teal rounded tile. It is distinct from the separate gear/return glyph font used in the capsule interface.

The build embeds `pinyinvim.ico` as both the executable's Windows icon and `PinyinVim.AppIcon`. The host loads that same resource for the tray and applies it to the editor window. No separate icon file is required beside the built executable.

## Files

| File | Use |
| --- | --- |
| `pinyinvim-icon.png` | Original square RGBA master; used by both project READMEs. |
| `pinyinvim-icon-512.png` | 512 px transparent PNG for documentation and presentation. |
| `pinyinvim-icon-256.png` | 256 px transparent PNG. |
| `pinyinvim.ico` | Windows icon with 16, 20, 24, 32, 40, 48, 64, 128 and 256 px frames. |
| `pinyinvim-icon-preview.png` | Light/dark preview showing small icons at their actual pixel sizes. |
| `export-icon.ps1` | Re-export sizes and formats from the unchanged master using Windows System.Drawing. |

## Generation and export

Created with the built-in imagegen tool for this project. The requested design was a single transparent Windows app icon: a deep blue-to-teal rounded tile, a floating ivory frosted note, a broad V-shaped editing stroke, a mint cursor and one writing line, with subtle depth and clear small-size shapes. No captions, watermarks or use of the existing Vim logo.

<details>
<summary>Exact generation prompt</summary>

```text
Use case: logo-brand. Create one beautiful, finished Windows desktop application icon for PinyinVim / 拼音浮笺, a floating Chinese-writing notepad with a real Vim editor and a refined frosted-glass interface. Design a distinctive, compact, calm productivity-app mark: a softly rounded-square tile in deep muted blue transitioning gently to teal, holding a luminous ivory frosted-glass note/page. Inside the page use a bold, elegant abstract V-shaped editing stroke plus a small mint insertion-cursor accent, with a short horizontal writing line above. The note may be slightly floating or layered, but keep the silhouette clean, front-facing and immediately legible. Subtle satin highlights and very soft depth, contemporary premium Windows app aesthetic, precise geometry and polished balanced proportions. Broad white/ivory shapes and strong contrast that remain recognizable at 16–32 px; absolutely no fine detail, noise, ornamental symbols, pen illustration, characters, text, captions, badge, watermark, desktop background or mockup. Not the Vim logo. Exactly one isolated centered icon, no variants or contact sheet. Rounded tile takes up about 90 percent of the square canvas with modest transparent margin; actual transparent alpha outside the tile, including corners. Deliver a high-resolution square RGBA icon suitable for PNG and ICO export.
```

</details>

The original generated PNG is preserved. Resizing and ICO encoding only produce format/size derivatives. To regenerate those derivatives on Windows:

```powershell
.\assets\branding\export-icon.ps1
```

No AI API or image generator is required to build or run PinyinVim. This export script does not build or package the application.
