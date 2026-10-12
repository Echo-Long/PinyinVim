set nocompatible encoding=utf-8 nomore
let &rtp = getcwd() . '/native/nvim,' . &rtp
let g:pinyinvim_words = 1
let g:pinyinvim_pinyin = 1
runtime plugin/chinese.vim
let s:count = 0
function! s:Run(keys, native, text, column) abort
  call feedkeys("\<Esc>", 'nxt')
  enew!
  call setline(1, a:text)
  call cursor(1, a:column)
  call setreg('"','')
  call setreg('a','')
  call setcharsearch({'char':'','forward':1,'until':0})
  " A failed mapped motion flushes queued mappings in Vim. Deliver the next
  " physical comma separately so this checks interactive repeat semantics.
  if a:keys ==# 'fd,' || a:keys ==# 'td,'
    call feedkeys(a:keys[:1], a:native ? 'nxt' : 'mxt')
    call feedkeys(',', a:native ? 'nxt' : 'mxt')
  else
    call feedkeys(a:keys, a:native ? 'nxt' : 'mxt')
  endif
  return [getline(1,'$'),getcurpos()[1:2],getreg('"'),getregtype('"'),getreg('a'),getcharsearch()]
endfunction
let commands = ['w','2w','b','3b','e','2e','ge','2ge','dw','2dw','d2w','de','db','cwX'."\<Esc>",'diw','2diw','daw','2daw','ciwX'."\<Esc>",'vwwy','viwy','viwawy','v2iwy','vawy','vtdy','vtdly','fd','2fd','Fd','2Fd','td','2td','Td','dfd','dtd','dFd','dTd','"adfd','fd;','fd,','td;','td;;','td,','Td;','dfd.','dtd.','dw.','ciwX'."\<Esc>".'.','f'."\<Esc>".'l','fz','dfz']
for txt in ['abcd def d d tail','alpha beta delta d word','one  two, three! d four','   lead dmiddle dtail   ']
  for column in [1,5,10,strlen(txt)]
    for keys in commands
      let expected = s:Run(keys,1,txt,column)
      let actual = s:Run(keys,0,txt,column)
      call assert_equal(expected, actual, string([txt,column,keys]))
      let s:count += 1
    endfor
  endfor
endfor
call writefile([json_encode({'checks':s:count,'failures':v:errors})],'native/tests/equivalence.json')
if !empty(v:errors)
  call writefile(v:errors, 'native/tests/equivalence.failures')
  cquit 1
endif
qa!
