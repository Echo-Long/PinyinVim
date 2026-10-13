set encoding=utf-8 nomore
let g:copy_calls = 0
function! PinyinClipboardCopied() abort
  let g:copy_calls += 1
endfunction
try
lua << EOF
local value = {}
vim.g.clipboard = {
  name = 'PinyinVim isolated memory clipboard',
  copy = {['+'] = function(lines, regtype) value = {lines, regtype} end, ['*'] = function(lines, regtype) value = {lines, regtype} end},
  paste = {['+'] = function() return value end, ['*'] = function() return value end},
  cache_enabled = 0
}
local capsules = dofile('native/prototypes/capsules/lua/pinyinvim/capsules.lua')
vim.g.pinyinvim_route = '剪贴板提交'
assert(capsules.submission_hint():find('仅复制到剪贴板', 1, true))
vim.g.pinyinvim_route = '粘贴提交'
assert(capsules.submission_hint():find('粘贴到原输入框', 1, true))
capsules.draft = vim.api.nvim_get_current_buf()
vim.api.nvim_buf_set_lines(capsules.draft,0,-1,false,{'fixture text'})
capsules.copy()
assert(vim.g.copy_calls == 1, 'explicit draft copy emits one clipboard notification event')
assert(table.concat(value[1], '\n') == 'fixture text', 'copy uses the Neovim clipboard provider')
EOF
lua dofile('native/tests/capsule-pending.lua')
catch
  call add(v:errors, v:exception)
endtry
if len(v:errors)
  call writefile(v:errors, 'native/tests/capsule-feedback.failures')
  cquit 1
endif
qa!
