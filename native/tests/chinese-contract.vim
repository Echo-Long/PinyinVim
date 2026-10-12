set nocompatible encoding=utf-8 nomore
let &rtp = getcwd() . '/native/nvim,' . &rtp
let g:pinyinvim_words = 1
let g:pinyinvim_pinyin = 1
runtime plugin/chinese.vim
let checks = 0
function! s:Case(text, keys, expected, column) abort
  call feedkeys("\<Esc>", 'nxt')
  enew!
  call setline(1,a:text)
  call cursor(1,1)
  call feedkeys(a:keys,'mxt')
  call assert_equal(a:expected, type(a:expected) == v:t_list ? getline(1,'$') : getline(1), string([a:text,a:keys]))
  if a:column >= 0
    call assert_equal(a:column,charidx(getline(1),col('.')-1),string([a:text,a:keys,'cursor']))
  endif
endfunction
call s:Case('甲中知字终结','2fz','甲中知字终结',2)
call s:Case('甲中知字终结','dfz','知字终结',0)
call s:Case('甲中知字终结','dtz','中知字终结',0)
call s:Case('甲中知字终结','dfz.','终结',0)
call s:Case('甲中知字终结','fz;,;','甲中知字终结',2)
call s:Case('甲中知字终结','tz;','甲中知字终结',1)
call s:Case('甲中知字终结','v2fzly','甲中知字终结',0)
call assert_equal('甲中知字',getreg('"'),'Visual + count + find + further motion + yank')
call s:Case('中文计算机学习','2w','中文计算机学习',5)
call s:Case('中文计算机学习','d2w','学习',0)
call s:Case('中文计算机学习','dw.','学习',0)
call s:Case('中文计算机学习','diw.','学习',0)
call s:Case('中文 计算机学习',"cwX\<Esc>",'X 计算机学习',0)
call s:Case('中文计算机学习','dw2.','',0)
call s:Case('中文计算机学习','diw2.','',0)
call s:Case('中文计算机学习','2wdw','中文计算机',4)
call s:Case('中文计算机学习','wcwX'."\<Esc>",'中文X学习',2)
call s:Case('甲 中文',"cwX\<Esc>",'X 中文',0)
call s:Case('中文计算机学习','3e','中文计算机学习',6)
call s:Case('中文计算机学习','2wge','中文计算机学习',4)
call s:Case('中文计算机学习','daw.','学习',0)
call s:Case(['中文','计算机学习'],'2w',['中文','计算机学习'],-1)
call assert_equal([2,10],getcurpos()[1:2],'word motion across line break')
call s:Case(['中文','计算机学习'],'dw','',0)
call assert_equal(['','计算机学习'],getline(1,'$'),'dw does not consume next line')
" CJK punctuation is a literal find target and an independent word boundary.
call s:Case('中文，测试。你好！','w','中文，测试。你好！',2)
call setline(1, '中文，测试。你好！')
call cursor(1,1)
call pinyinvim#chinese#MoveFind('f','，','n',1,0)
call assert_equal(2,charidx(getline(1),col('.')-1),'f finds Chinese comma')
call writefile([json_encode({'checks':48,'failures':v:errors})],'native/tests/chinese-contract.json')
if !empty(v:errors)
  call writefile(v:errors,'native/tests/chinese-contract.failures')
  cquit 1
endif
qa!
