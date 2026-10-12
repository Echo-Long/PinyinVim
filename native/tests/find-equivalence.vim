set nocompatible encoding=utf-8 nomore
let &rtp = getcwd() . '/native/nvim,' . &rtp
let g:pinyinvim_words = 1
let g:pinyinvim_pinyin = 1
runtime plugin/chinese.vim
let s:alphabet = {'甲':'j','中':'z','知':'z','字':'z','终':'z','结':'j'}
function! s:Latin(text) abort
  return join(map(split(a:text,'\zs'),'get(s:alphabet,v:val,v:val)'), '')
endfunction
function! s:Run(text, column, keys, native) abort
  call feedkeys("\<Esc>",'nxt')
  enew!
  call setline(1,a:text)
  call cursor(1,byteidx(a:text,a:column)+1)
  call setreg('"','')
  call setreg('a','')
  call setcharsearch({'char':'','forward':1,'until':0})
  call feedkeys(a:keys,a:native ? 'nxt' : 'mxt')
  return [s:Latin(getline(1)),charidx(getline(1),col('.')-1),s:Latin(getreg('"')),getregtype('"'),s:Latin(getreg('a'))]
endfunction
let checks = 0
let original_cpo = &cpoptions
let commands = ['fz','Fz','tz','Tz','2fz','2Fz','2tz','2Tz','9fz','9tz','dfz','dFz','dtz','dTz','d2fz','d2Fz','d2tz','d2Tz','cfzX'."\<Esc>",'ctzX'."\<Esc>",'dfz.','dtz.','"adfz','vfzly','vtzly','vFzhy','vTzhy','v2fzy','fz;', 'fz,','tz;', 'tz2;','Tz;', 'fz;,;', 'f'."\<Esc>".'l','yfzhp','qadfzq@a']
let commands += ['cfzX'."\<Esc>".'.','ctzX'."\<Esc>".'.','cTzX'."\<Esc>",'cFzX'."\<Esc>",'dfz2.','2dfz.','dfzl.']
for txt in ['甲中知字终结','甲z中j知 字终结','字甲结中知甲终']
  for column in range(strchars(txt))
    for keys in commands
      let expected = s:Run(s:Latin(txt),column,keys,1)
      let actual = s:Run(txt,column,keys,0)
      call assert_equal(expected,actual,string([txt,column,keys]))
      let checks += 1
    endfor
  endfor
endfor
sleep 1m
call assert_equal(original_cpo,&cpoptions,'temporary empty-region guard must restore user options')
let checks += 1
call writefile([json_encode({'checks':checks,'failures':v:errors})],'native/tests/find-equivalence.json')
if !empty(v:errors)
  call writefile(v:errors,'native/tests/find-equivalence.failures')
  cquit 1
endif
qa!
