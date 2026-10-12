" Host-owned settings window. The draft, mode, selection and target stay intact.
function! PinyinSettingsView() abort
  call s:Send({'kind':'settings'})
endfunction

function! PinyinCapsuleSetSettings(values) abort
  let l:next = extend(deepcopy(s:settings), a:values)
  call s:Send({'kind':'settings-update', 'settings':l:next}, 1)
endfunction

function! PinyinCapsuleGetSettings() abort
  return deepcopy(s:settings)
endfunction
