set nocompatible
set nomore
set encoding=utf-8
set showcmd
let &runtimepath = fnameescape(getcwd() . '/native/nvim') . ',' . &runtimepath
let g:pinyinvim_words = 1
let g:pinyinvim_pinyin = 1
source native/nvim/plugin/chinese.vim
let g:failures = []
let g:assertions = 0

function! s:Check(name, actual, expected) abort
  let g:assertions += 1
  if a:actual !=# a:expected
    call add(g:failures, a:name . ': got ' . string(a:actual) . ', expected ' . string(a:expected))
  endif
endfunction

function! s:Reset(text) abort
  enew!
  call setline(1, a:text)
  call cursor(1, 1)
endfunction

call s:Reset('中文编辑测试 abc')
normal w
call s:Check('dictionary w', col('.'), 7)
normal b
call s:Check('dictionary b', col('.'), 1)
normal e
call s:Check('dictionary e', col('.'), 4)
normal ge
call s:Check('dictionary ge', col('.'), 1)

call s:Reset('中文编辑测试 abc')
normal diw
call s:Check('operator iw', getline(1), '编辑测试 abc')
call s:Reset('中文 编辑')
normal daw
call s:Check('operator aw includes trailing space', getline(1), '编辑')

call s:Reset('one two three')
let g:ascii_start = [line('.'), col('.')]
normal 2w
let g:ascii_custom = [line('.'), col('.')]
call s:Check('ASCII native w', g:ascii_custom, [1, 9])

call s:Reset('中文编辑测试')
normal 2dw
call s:Check('operator count', getline(1), '测试')

call s:Reset('中文编辑测试')
normal 2diw
call s:Check('text object count', getline(1), '测试')

call s:Reset('中文编辑测试 中文编辑测试')
normal dw
normal .
call s:Check('dot repeat', getline(1), '测试 中文编辑测试')

call s:Reset('中文编辑测试')
normal "adw
call s:Check('named register', getreg('a'), '中文')
call s:Check('register operator result', getline(1), '编辑测试')

call s:Reset('中文编辑测试 中文编辑测试')
call feedkeys('qadwq', 'xt')
normal! 0
call feedkeys('@a', 'xt')
call s:Check('macro replay', getline(1), '测试 中文编辑测试')

call s:Reset('汉语测试文本')
call feedkeys('fw', 'xt')
call s:Check('pinyin initial find', col('.'), 13)
call feedkeys(';', 'xt')
call s:Check('pinyin find repeat', col('.'), 13)

call s:Reset('abc xyz')
normal! 0
normal! w
let g:ascii_native = [line('.'), col('.')]
call s:Reset('abc xyz')
normal w
call s:Check('ASCII differential w', [line('.'), col('.')], g:ascii_native)

for g:key in ['w', 'b', 'e', 'ge']
  call s:Reset('alpha, beta_2 gamma')
  call cursor(1, 8)
  execute 'normal! ' . g:key
  let g:ascii_native = [line('.'), col('.')]
  call s:Reset('alpha, beta_2 gamma')
  call cursor(1, 8)
  execute 'normal ' . g:key
  call s:Check('ASCII differential ' . g:key, [line('.'), col('.')], g:ascii_native)
endfor
unlet g:key

call s:Reset('alpha beta gamma')
normal! dw
let g:ascii_native_line = getline(1)
call s:Reset('alpha beta gamma')
normal dw
call s:Check('ASCII differential dw', getline(1), g:ascii_native_line)

call s:Reset('alpha beta gamma')
normal! diw
let g:ascii_native_line = getline(1)
call s:Reset('alpha beta gamma')
normal diw
call s:Check('ASCII differential diw', getline(1), g:ascii_native_line)

call s:Reset('汉语测试文本的内容')
call feedkeys('vtd', 'xt')
call s:Check('visual till initial position', col('.'), 16)

call s:Reset('abc xbc')
call feedkeys('fx', 'xt')
call s:Check('ASCII exact find', col('.'), 5)

if !empty(g:failures)
  call writefile(g:failures, 'native/tests/chinese.failures')
  cquit 1
endif
call delete('native/tests/chinese.failures')
call writefile([json_encode({'passed': 1, 'assertions': g:assertions, 'failures': g:failures})], 'native/tests/chinese.results.json')
qa!
