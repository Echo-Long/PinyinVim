" PinyinVim's Neovim profile. Neovide owns the GUI and Neovim owns every
" editing state; the native host only exchanges lifecycle messages.
set nocompatible
set encoding=utf-8
set noswapfile
set hidden
set number norelativenumber numberwidth=3 cursorline
set showcmd showcmdloc=statusline noshowmode cmdheight=0
set laststatus=2
set shortmess+=I
set title titlestring=拼音浮笺\ \ ·\ \ Esc\ 收起
execute 'set guifont=Consolas,等距更纱黑体\ SC:h14'
if exists('g:neovide')
  let g:neovide_opacity = 0.85
  let g:neovide_normal_opacity = 0.85
  let g:neovide_corner_preference = 'round'
  let g:neovide_confirm_quit = v:false
  let g:neovide_detach_on_quit = 'always_quit'
  let g:neovide_remember_window_size = v:false
  let g:neovide_position_animation_length = 0
  let g:neovide_scroll_animation_length = 0
  " Keep the GPU frame loop active so a native window resize is painted
  " continuously instead of only before and after the drag gesture.
  let g:neovide_no_idle = v:true
  let g:neovide_refresh_rate = 120
  let g:neovide_refresh_rate_idle = 30
endif
let s:root = fnamemodify(expand('<sfile>:p'), ':h')
execute 'set runtimepath^=' . fnameescape(s:root)
let g:pinyinvim_words = 1
let g:pinyinvim_pinyin = 1
let g:pinyinvim_pending = ''
runtime plugin/chinese.vim
runtime plugin/bridge.vim
