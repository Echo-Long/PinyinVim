set nocompatible encoding=utf-8 nomore
let &rtp = getcwd() . '/native/nvim,' . &rtp
let g:pinyinvim_words = 1
let g:pinyinvim_pinyin = 1
let g:neovide = v:true
runtime plugin/chinese.vim
runtime plugin/bridge.vim

call assert_equal(v:false, get(g:, 'neovide_input_ime', v:false), 'Neovide IME is disabled before Insert')
doautocmd InsertEnter
call assert_equal(v:true, get(g:, 'neovide_input_ime', v:false), 'Neovide IME is enabled in Insert')
doautocmd InsertLeavePre
call assert_equal(v:false, get(g:, 'neovide_input_ime', v:true), 'Neovide IME is disabled before leaving Insert')

let s:bridge_sid = 0
for s:line in split(execute('scriptnames'), "\n")
  if s:line =~# 'native[/\\]nvim[/\\]plugin[/\\]bridge\.vim$'
    let s:bridge_sid = str2nr(matchstr(s:line, '^\s*\zs\d\+'))
  endif
endfor
if s:bridge_sid > 0
  execute 'call <SNR>' . s:bridge_sid . '_ApplySettings({"ime": 0})'
  call assert_equal(v:true, get(g:, 'neovide_input_ime', v:false), 'Disabling automatic switching preserves manual IME input')
  execute 'call <SNR>' . s:bridge_sid . '_ApplySettings({"ime": 1})'
endif

let g:pinyinvim_pending = 'dt'
call assert_match('^dt · 输入中文、标点或拼音首字母', PinyinActivity(), 'pending command is visible in statusline activity')
let g:pinyinvim_pending = ''
call assert_equal('剪贴板提交', get(g:, 'pinyinvim_route', ''), 'clipboard route is the safe default')
call assert_match('PinyinHide', maparg('<Esc>', 'n'), 'Normal Esc is wired to the host hide action')
call PinyinSettingsClick(0, 1, 'l', '')
call PinyinSubmitClick(0, 1, 'l', '')
call PinyinSettingsBack()

enew!
call setline(1, 'draft')
setlocal nomodified
call PinyinSettingsView()
call assert_equal(1, get(b:, 'pinyinvim_settings', 0), 'settings replaces the editor view')
call assert_match('拼音浮笺  设置', getline(1), 'settings view has its own content')
call PinyinSettingsBack()
call assert_equal(0, get(b:, 'pinyinvim_settings', 0), 'settings returns to the same editor window')

enew!
call setline(1, '中文，测试,你好。')
call cursor(1, 1)
call pinyinvim#chinese#MoveFind('f', '，', 'n', 1, 0)
call assert_equal(2, charidx(getline(1), col('.') - 1), 'Chinese comma is literal')
call cursor(1, 1)
call pinyinvim#chinese#MoveFind('f', ',', 'n', 1, 0)
call assert_equal(5, charidx(getline(1), col('.') - 1), 'ASCII comma is a separate literal')
call cursor(1, 1)
call pinyinvim#chinese#MoveFind('f', '。', 'n', 1, 0)
call assert_equal(8, charidx(getline(1), col('.') - 1), 'Chinese full stop is literal')
call cursor(1, 1)
call pinyinvim#chinese#MoveFind('f', '.', 'n', 1, 0)
call assert_equal(0, charidx(getline(1), col('.') - 1), 'ASCII full stop does not match Chinese full stop')

if !empty(v:errors)
  call writefile(v:errors, 'native/tests/nvim-contract.failures')
  cquit 1
endif
call writefile(['Neovim punctuation contract: 4 focused checks passed'], 'native/tests/nvim-contract.result')
qa!
