set nocompatible encoding=utf-8 nomore
let &rtp = getcwd() . '/native/nvim,' . &rtp
let g:pinyinvim_words = 1
let g:pinyinvim_pinyin = 1
runtime plugin/chinese.vim
let s:checks = 0

function! s:Run(lines, keys, ...) abort
  call feedkeys("\<Esc>", 'nxt')
  enew!
  call setline(1, a:lines)
  call cursor(1, 1)
  call setreg('"', '')
  call feedkeys(a:keys, a:0 && a:1 ? 'nxt' : 'mxt')
  return [getline(1, '$'), getcurpos()[1:2], getreg('"'), getregtype('"')]
endfunction

function! s:Equal(expected, actual, name) abort
  let s:checks += 1
  call assert_equal(a:expected, a:actual, a:name)
endfunction

for keys in ['fa', 'ta', '2fa', 'Fa', 'Ta', 'dfa', 'dta', 'cfaX'."\<Esc>", 'vfaY', 'fa;', 'fa,', 'dfa.', 'qadfaq@a']
  let text = ['甲abc a A 🤖']
  call s:Equal(s:Run(text, keys, 1), s:Run(text, keys), 'literal ASCII in mixed text: '.keys)
endfor
for text in ['甲x́ax́a', '甲á bá', '甲中️a中️']
  for keys in ['fx', 'fa', '2fa', 'dfa', 'f中']
    call s:Equal(s:Run([text], keys, 1), s:Run([text], keys), 'literal base character with composing marks: '.string([text, keys]))
  endfor
endfor
call s:Equal([2, 7], s:Run(['abc', '中文计算机学习'], '2w')[1], 'count crosses from ASCII into Chinese tokens')
call s:Equal(s:Run(['abc', '中文计算机学习'], 'ww'), s:Run(['abc', '中文计算机学习'], '2w'), '2w and w w share boundaries')
call s:Equal([1, 16], s:Run(['中文计算机学习', 'abc'], 'j0b')[1], 'backwards entry lands at last Chinese token')
call s:Equal(['学习'], s:Run(['中文', '计算机学习'], '2diw')[0], 'iw count includes next line')
call s:Equal(['学习'], s:Run(['中文', '计算机学习'], '2daw')[0], 'aw count includes next line')
call s:Equal(['学习'], s:Run(['alpha', '中文学习'], '2diw')[0], 'ASCII text object count enters Chinese line')
call s:Equal('中文\n计算机', substitute(s:Run(['中文', '计算机学习'], '2yiw')[2], "\n", '\\n', 'g'), 'cross-line yank preserves exact register range')
call s:Equal(['中文', '计算机学习'], s:Run(['中文', '计算机学习'], '2diwu')[0], 'cross-line delete has one native undo')
call s:Equal(['中文', '计算机学习'], s:Run(['中文', '计算机学习'], '2ciwX'."\<Esc>u")[0], 'cross-line change has one native undo')
let blank_range = s:Run(['中文计算机', '', '学习'], '3yiw')
call s:Equal("中文计算机\n\n", blank_range[2], 'refined word count includes an empty-line object')
call s:Equal('V', blank_range[3], 'whole lines keep a linewise yank register')
call s:Equal(['学习'], s:Run(['中文计算机', '', '学习'], '3diw')[0], 'linewise counted object removes only its selected lines')

" A Chinese line nearby must not alter native English/blank-line editing.
for lines in [['alpha beta delta', '中文学习'], ['', '中文学习'], ['alpha', '', '中文学习']]
  for keys in ['cwX'."\<Esc>", 'dw', 'de', 'diw', 'daw', 'yiw', 'yaw', 'e', 'w', '2e']
    if empty(lines[0]) && index(['de', 'daw', 'yaw'], keys) >= 0 | continue | endif
    " e/w may intentionally refine a Chinese destination; compare edits on
    " the ASCII starting line and their text/registers, not that cursor.
    let expected = s:Run(lines, keys, 1)
    let actual = s:Run(lines, keys)
    if keys ==# '2e' || keys ==# 'e' || keys ==# 'w' | continue | endif
    call s:Equal(expected[0], actual[0], 'native prefix buffer: '.string([lines, keys]))
    call s:Equal(expected[2:], actual[2:], 'native prefix registers: '.string([lines, keys]))
  endfor
endfor
" When Chinese tokens already agree with Vim, even cross-line whitespace,
" dot replay and linewise register types must stay on the native path.
for lines in [['中文','','计算机 学习'], ['中文  ',' 计算机 学习'], ['中文','','','计算机 学习'], ['alpha beta','中文 计算机 学习'], ['中文','ASCII1!','计算机 学习']]
  for keys in ['w','2w','3w','4w','5w','e','2e','3e','4e','G0b','G0ge','dw','2dw','d2w','de','2de','cwX'."\<Esc>",'2cwX'."\<Esc>",'diw','2diw','3diw','4diw','daw','2daw','3daw','yiw','2yiw','3yiw','yaw','2yaw','diw.','2diw.','daw.','2daw.']
    call s:Equal(s:Run(lines, keys, 1), s:Run(lines, keys), 'native cross-line contract: '.string([lines, keys]))
  endfor
endfor
call writefile([json_encode({'checks': s:checks, 'failures': v:errors})], 'native/tests/navigation-regressions.json')
if !empty(v:errors)
  call writefile(v:errors, 'native/tests/navigation-regressions.failures')
  cquit 1
endif
qa!
