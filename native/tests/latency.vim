set nocompatible encoding=utf-8 nomore
let &rtp = getcwd() . '/native/nvim,' . &rtp
let g:pinyinvim_words = 1
let g:pinyinvim_pinyin = 1
function! s:Measure(keys) abort
  let samples = []
  for i in range(1,200)
    enew!
    call setline(1,'我喜欢中文编辑测试。这里包含汉字与 ASCII words。')
    call cursor(1,1)
    let begin = reltime()
    call feedkeys(a:keys,'xt')
    call add(samples, reltimefloat(reltime(begin))*1000)
  endfor
  call sort(samples,'f')
  return {'p50_ms':samples[99], 'p95_ms':samples[189], 'max_ms':samples[-1], 'samples':len(samples)}
endfunction
let result = {'scope':'headless Neovim command processing; not physical input, IME, painting or Windows paste latency'}
let result.vanilla_insert_escape = s:Measure("ix\<Esc>")
let start = reltime()
runtime plugin/chinese.vim
let result.plugin_preload_ms = reltimefloat(reltime(start))*1000
let result.plugin_insert_escape = s:Measure("ix\<Esc>")
let result.pinyin_find = s:Measure('fz')
let result.chinese_word = s:Measure('w')
call writefile([json_encode(result)],'native/tests/latency.json')
qa!
