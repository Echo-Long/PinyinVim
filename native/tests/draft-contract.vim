set encoding=utf-8 nomore
let &rtp = getcwd() . '/native/nvim,' . &rtp
let s:profile = tempname()
call mkdir(s:profile, 'p')
let $PINYINVIM_DRAFT = s:profile . '/draft.txt'
let $PINYINVIM_TOKEN = 'isolated-draft-test'
call writefile(['already submitted'], $PINYINVIM_DRAFT)
execute 'edit ' . fnameescape($PINYINVIM_DRAFT)
runtime plugin/bridge.vim
let s:sid = 0
for s:script in getscriptinfo()
  if s:script.name =~# 'nvim[/\\]plugin[/\\]bridge\.vim$' | let s:sid = s:script.sid | endif
endfor
function! s:Handle(message) abort
  let message = extend({'token': $PINYINVIM_TOKEN}, a:message)
  execute 'call <SNR>' . s:sid . '_Handle(message)'
endfunction
call s:Handle({'kind':'session','session':1,'clear':1,'clearHash':sha256('an older submitted snapshot'),'settings':{'clearOnClose':1,'ime':1}})
call assert_equal(['already submitted'], getline(1,'$'), 'a late clear cannot discard newer buffer contents')
call assert_equal(['already submitted'], readfile($PINYINVIM_DRAFT), 'a late clear cannot discard newer disk contents')
call s:Handle({'kind':'session','session':1,'clear':1,'clearHash':sha256('already submitted'),'settings':{'clearOnClose':1,'ime':1}})
call assert_equal([''], getline(1,'$'), 'submitted buffer clears')
call assert_equal([], readfile($PINYINVIM_DRAFT), 'submitted disk draft clears too')
call setline(1, 'editing with clear-on-exit enabled')
doautocmd TextChanged
call assert_equal(['editing with clear-on-exit enabled'], readfile($PINYINVIM_DRAFT), 'clear-on-exit keeps crash recovery while editing')
call assert_false(&modified, 'only successful persistence clears modified flag')
call setline(1, 'hidden draft')
call PinyinHide()
call assert_equal(['hidden draft'], readfile($PINYINVIM_DRAFT), 'Esc/hide always persists; never clears')
execute 'call <SNR>' . s:sid . '_Leaving()'
call assert_equal(['hidden draft'], readfile($PINYINVIM_DRAFT), 'ordinary Neovim exit is not tray clear')
call s:Handle({'kind':'submit-ack','session':1,'ok':1,'reason':'copied-fallback'})
call assert_equal('已复制，请手动粘贴', g:pinyinvim_notice, 'failed paste has explicit copied outcome')
call s:Handle({'kind':'submit-ack','session':1,'ok':1,'reason':'paste-sent'})
call assert_equal('已发送粘贴', g:pinyinvim_notice, 'dispatch is not claimed as target acceptance')
call s:Handle({'kind':'submit-ack','session':0,'ok':1,'reason':'copied'})
call assert_equal('已发送粘贴', g:pinyinvim_notice, 'late acknowledgement cannot overwrite current session')
let g:neovide = v:true
call s:Handle({'kind':'settings','settings':{'ime':0}})
doautocmd InsertEnter
call assert_true(g:neovide_input_ime, 'manual Chinese input works with automatic policy disabled')
doautocmd InsertLeavePre
call assert_true(g:neovide_input_ime, 'disabled policy does not force a mode switch')
call s:Handle({'kind':'settings','settings':{'ime':1}})
doautocmd InsertEnter
call assert_true(g:neovide_input_ime, 'automatic Insert enables IME')
doautocmd InsertLeavePre
call assert_false(g:neovide_input_ime, 'automatic Normal disables IME')
execute 'call <SNR>' . s:sid . '_SyncNeovideIme("VISUAL")'
call assert_false(g:neovide_input_ime, 'automatic Visual disables IME')
execute 'call <SNR>' . s:sid . '_SyncNeovideIme("REPLACE")'
call assert_true(g:neovide_input_ime, 'Replace keeps its input channel')
execute 'call <SNR>' . s:sid . '_SyncNeovideIme("COMMAND")'
call assert_true(g:neovide_input_ime, 'search/command Chinese input stays available')
call writefile([json_encode({'failures':v:errors})], 'native/tests/draft-contract.json')
if !empty(v:errors)
  call writefile(v:errors, 'native/tests/draft-contract.failures')
  cquit 1
endif
qa!
