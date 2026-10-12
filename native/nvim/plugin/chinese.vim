if exists('g:loaded_pinyinvim_chinese_plugin')
  finish
endif
let g:loaded_pinyinvim_chinese_plugin = 1
let g:pinyinvim_pending = ''
for s:mode in ['n', 'x', 'o']
  for s:key in ['w', 'b', 'e', 'ge']
    execute s:mode . 'noremap <expr> ' . s:key . ' pinyinvim#chinese#Word(' . string(s:key) . ',' . string(s:mode) . ')'
  endfor
  for s:key in ['f', 'F', 't', 'T']
    execute s:mode . 'noremap <expr> ' . s:key . ' pinyinvim#chinese#Find(' . string(s:key) . ',' . string(s:mode) . ')'
  endfor
  execute s:mode . 'noremap <expr> ; pinyinvim#chinese#RepeatFind(0,' . string(s:mode) . ')'
  execute s:mode . 'noremap <expr> , pinyinvim#chinese#RepeatFind(1,' . string(s:mode) . ')'
  if s:mode !=# 'n'
    for s:key in ['iw', 'aw']
      execute s:mode . 'noremap <expr> ' . s:key . ' pinyinvim#chinese#Object(' . (s:key ==# 'iw' ? 1 : 0) . ',' . string(s:mode) . ')'
    endfor
  endif
endfor
unlet s:mode s:key
call pinyinvim#chinese#Preload()
