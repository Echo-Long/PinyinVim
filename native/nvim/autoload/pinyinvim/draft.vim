" One managed draft, written next to its destination before atomic rename.
function! pinyinvim#draft#Save(path, lines) abort
  let destination = fnamemodify(a:path, ':p')
  let pending = destination . '.' . getpid() . '.pending'
  try
    if writefile(a:lines, pending) != 0 || rename(pending, destination) != 0
      throw 'PinyinVim: draft could not be saved'
    endif
  finally
    if filereadable(pending) | call delete(pending) | endif
  endtry
endfunction
