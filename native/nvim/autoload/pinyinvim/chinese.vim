if exists('g:autoloaded_pinyinvim_chinese')
  finish
endif
let g:autoloaded_pinyinvim_chinese = 1
let s:words = {}
let s:initials = {}
let s:cache = {}
let s:cache_id = []
let s:last_find = {}
let s:data_root = fnamemodify(expand('<sfile>:p'), ':h:h:h') . '/data'

function! pinyinvim#chinese#Preload() abort
  if !empty(s:words) | return | endif
  for word in readfile(s:data_root . '/chinese-words.txt')
    let s:words[word] = 1
  endfor
  for row in readfile(s:data_root . '/chinese-initials.txt')
    let parts = split(row, "\t")
    if len(parts) == 2 | let s:initials[parts[0]] = parts[1] | endif
  endfor
endfunction

function! s:IsHan(char) abort
  let n = char2nr(a:char)
  return (n >= 0x3400 && n <= 0x9fff) || (n >= 0xf900 && n <= 0xfaff)
        \ || (n >= 0x20000 && n <= 0x3134f)
endfunction

function! s:HasHan(text) abort
  for char in split(a:text, '\zs')
    if s:IsHan(char) | return 1 | endif
  endfor
  return 0
endfunction

" Chinese punctuation must behave as a boundary of its own.  Vim's \k
" classification only covers ASCII/locale word characters, so relying on it
" makes `w`, `b`, `e`, `iw`, and `aw` treat adjacent CJK punctuation as one
" opaque symbol run.  Keep the list explicit and Unicode based so the same
" rule works regardless of the Windows locale.
function! s:IsPunctuation(char) abort
  let n = char2nr(a:char)
  return a:char =~# '^[，。！？；：、（）［］【】《》〈〉「」『』“”‘’…—～·「」〝〞〟]+$'
        \ || (n >= 0x2000 && n <= 0x206f)
        \ || (n >= 0x3000 && n <= 0x303f)
        \ || (n >= 0xff01 && n <= 0xff65)
        \ || (n >= 0x2e00 && n <= 0x2e7f)
endfunction

function! s:Kind(char) abort
  if s:IsHan(a:char) | return 'han' | endif
  if a:char =~# '\s' | return 'space' | endif
  if a:char =~# '\k' | return 'word' | endif
  return s:IsPunctuation(a:char) ? 'punct' : 'symbol'
endfunction

" Character offsets, inclusive ends. Only the requested line is tokenized.
function! s:Tokens(lnum) abort
  let id = [bufnr('%'), b:changedtick, &l:iskeyword]
  if s:cache_id !=# id
    let s:cache = {}
    let s:cache_id = id
  endif
  let key = string(a:lnum)
  if has_key(s:cache, key) | return s:cache[key] | endif
  let chars = split(getline(a:lnum), '\zs')
  let tokens = []
  let i = 0
  while i < len(chars)
    let start = i
    if s:IsHan(chars[i])
      let size = min([8, len(chars) - i])
      while size > 1 && !has_key(s:words, join(chars[i : i + size - 1], ''))
        let size -= 1
      endwhile
      let i += size
      let kind = 'han'
    else
      let kind = s:Kind(chars[i])
      let i += 1
      " Keep each punctuation mark addressable by f/t and as an operator
      " boundary.  Other non-Han runs retain Vim's native grouping.
      if kind ==# 'punct'
        call add(tokens, [start, i - 1, kind])
        continue
      endif
      while i < len(chars) && !s:IsHan(chars[i])
        let next = s:Kind(chars[i])
        if next !=# kind | break | endif
        let i += 1
      endwhile
    endif
    call add(tokens, [start, i - 1, kind])
  endwhile
  let s:cache[key] = tokens
  return tokens
endfunction

function! s:Index() abort
  return max([0, charidx(getline('.'), col('.') - 1)])
endfunction

function! s:At(tokens, index) abort
  for i in range(len(a:tokens))
    if a:tokens[i][0] <= a:index && a:tokens[i][1] >= a:index | return i | endif
  endfor
  return -1
endfunction

function! s:Move(lnum, index) abort
  call cursor(a:lnum, max([0, byteidx(getline(a:lnum), a:index)]) + 1)
endfunction

function! s:Call(name, args) abort
  return "\<Cmd>call pinyinvim#chinese#" . a:name . '(' . join(map(copy(a:args), 'string(v:val)'), ',') . ")\<CR>"
        \ . (a:name ==# 'MoveFind' ? "\<Cmd>call pinyinvim#chinese#RestoreOptions()\<CR>" : '')
endfunction

function! pinyinvim#chinese#RestoreOptions(...) abort
  if exists('s:empty_guard_options')
    let &cpoptions = s:empty_guard_options
    unlet s:empty_guard_options
  endif
endfunction

function! s:CancelEmptyChange() abort
  " A function motion with no movement normally lets c enter Insert. Ask Vim
  " to reject that empty range, including during native dot replay. Restore
  " immediately after the motion, or on the next event loop if failure flushed
  " the remainder of the mapping. No dot/insert/undo command is remapped.
  if &cpoptions !~# 'E'
    let s:empty_guard_options = &cpoptions
    let &cpoptions .= 'E'
    call timer_start(0, function('pinyinvim#chinese#RestoreOptions'))
  endif
endfunction

" A counted native motion can enter a Chinese line even when it starts on
" an ASCII line. Inspect only the lines that its steps could reach; keep
" completely non-Chinese motions on Vim's original implementation.
function! s:NeedsWords(key, count) abort
  if !get(g:, 'pinyinvim_words', 1) | return 0 | endif
  let dir = a:key ==# 'b' || a:key ==# 'ge' ? -1 : 1
  let lnum = line('.')
  let remaining = a:count + 1
  while lnum >= 1 && lnum <= line('$') && remaining > 0
    let text = getline(lnum)
    if s:HasHan(text)
      " If token boundaries already agree with Vim, leave its counted
      " objects, blank-line rules and linewise registers entirely native.
      let chars = split(text, '\zs')
      let tokens = s:Tokens(lnum)
      for i in range(1, len(tokens) - 1)
        let left = tokens[i - 1]
        let right = tokens[i]
        if left[2] ==# 'space' || right[2] ==# 'space' | continue | endif
        if (chars[left[1]] =~# '\k') == (chars[right[0]] =~# '\k') | return 1 | endif
      endfor
    endif
    if text =~# '\S' | let remaining -= 1 | endif
    let lnum += dir
  endwhile
  return 0
endfunction

function! pinyinvim#chinese#Word(key, context) abort
  " dw/cw on one empty line are native linewise operations, not a range
  " into the following Chinese word.
  if a:context ==# 'o' && a:key ==# 'w' && v:count1 == 1 && empty(getline('.')) | return a:key | endif
  if !s:NeedsWords(a:key, v:count1) | return a:key | endif
  return s:Call('MoveWord', [a:key, a:context, v:count1])
endfunction

" Use Vim's own motion to cross lines; refine destinations within a Han run.
function! s:StepWord(key) abort
  let origin = getpos('.')
  let native_line = !s:HasHan(getline('.'))
  let index = s:Index()
  let tokens = s:Tokens(line('.'))
  let candidates = []
  for token in tokens
    if token[2] ==# 'space' | continue | endif
    let dest = a:key ==# 'w' || a:key ==# 'b' ? token[0] : token[1]
    if (a:key ==# 'w' || a:key ==# 'e') ? dest > index : dest < index
      call add(candidates, dest)
    endif
  endfor
  if !native_line && !empty(candidates)
    call s:Move(line('.'), (a:key ==# 'w' || a:key ==# 'e') ? candidates[0] : candidates[-1])
    return 0
  endif
  let eof_word = a:key ==# 'w' && line('.') == line('$')
  execute 'normal! ' . a:key
  if line('.') != origin[1] && (a:key ==# 'e' || a:key ==# 'b')
    let tokens = filter(copy(s:Tokens(line('.'))), 'v:val[2] !=# "space"')
    if !empty(tokens) | call s:Move(line('.'), a:key ==# 'e' ? tokens[0][1] : tokens[-1][0]) | endif
  endif
  return eof_word
endfunction

function! s:Previous(pos) abort
  let index = charidx(getline(a:pos[1]), a:pos[2] - 1)
  if index > 0 | return [0, a:pos[1], byteidx(getline(a:pos[1]), index - 1) + 1, 0] | endif
  if a:pos[1] > 1
    let lnum = a:pos[1] - 1
    return [0, lnum, max([1, strlen(getline(lnum))]), 0]
  endif
  return copy(a:pos)
endfunction

function! s:Select(start, end) abort
  call setpos('.', a:start)
  normal! v
  call setpos('.', a:end)
  if &selection ==# 'exclusive'
    normal! l
  endif
endfunction

function! s:SelectWordRange(start, end, context) abort
  let last = getline(a:end[1])
  let first_nonblank = max([1, match(getline(a:start[1]), '\S') + 1])
  let at_eol = empty(last) || a:end[2] >= byteidx(last, strchars(last, 1) - 1) + 1
  " Native operators preserve linewise registers when whole lines are
  " consumed. An explicit characterwise Visual range would lose that type.
  if a:context ==# 'o' && a:end[1] > a:start[1] && a:start[2] <= first_nonblank
        \ && (empty(last) || (at_eol && index(['d', 'c'], v:operator) >= 0))
    call setpos('.', a:start)
    normal! V
    call setpos('.', a:end)
  else
    call s:Select(a:start, a:end)
  endif
endfunction

function! pinyinvim#chinese#MoveWord(key, context, count) abort
  let motion_count = v:count > 0 ? v:count : a:count
  let origin = getpos('.')
  let key = a:key
  let inclusive = key ==# 'e' || key ==# 'ge'
  if a:context ==# 'o' && v:operator ==# 'c' && key ==# 'w' && !empty(getline('.')) && getline('.')[col('.')-1:] !~# '^\s'
    let key = 'e'
    let inclusive = 1
  endif
  for step in range(motion_count)
    let prior = getpos('.')
    if step == 0 && a:context ==# 'o' && v:operator ==# 'c' && a:key ==# 'w' && key ==# 'e'
      let tokens = s:Tokens(line('.'))
      let token_index = s:At(tokens, s:Index())
      call s:Move(line('.'), tokens[token_index][1])
      let eof_word = 0
    else
      let eof_word = s:StepWord(key)
    endif
    " Vim dw stops at EOL on its final step, without eating the next line.
    if a:context ==# 'o' && key ==# 'w' && step == motion_count - 1 && line('.') != prior[1]
      call cursor(prior[1], max([1, strlen(getline(prior[1]))]))
      let inclusive = 1
    elseif a:context ==# 'o' && key ==# 'w' && eof_word
      let inclusive = 1
    endif
  endfor
  if a:context !=# 'o' | return | endif
  let dest = getpos('.')
  let forward = dest[1] > origin[1] || (dest[1] == origin[1] && dest[2] >= origin[2])
  let start = forward ? origin : dest
  let end = forward ? dest : origin
  if !inclusive
    if start ==# end | call setpos('.', origin) | return | endif
    let end = s:Previous(end)
  endif
  call s:SelectWordRange(start, end, a:context)
endfunction

function! pinyinvim#chinese#Object(inner, context) abort
  if a:inner && v:count1 == 1 && empty(getline('.')) | return 'iw' | endif
  if !s:NeedsWords('w', v:count1) | return a:inner ? 'iw' : 'aw' | endif
  return s:Call('SelectObject', [a:inner, a:context, v:count1])
endfunction

" Reuse the per-line token cache. A newline connects objects; empty lines
" remain individually countable objects, as in native iw/aw.
function! s:ObjectSpan(lnum, at) abort
  let tokens = s:Tokens(a:lnum)
  if empty(tokens)
    return [a:lnum, 0, a:lnum, 0, 'space']
  endif
  let token = tokens[a:at]
  return [a:lnum, token[0], a:lnum, token[1], token[2]]
endfunction

function! s:NextObject(span) abort
  let tokens = s:Tokens(a:span[2])
  for i in range(len(tokens))
    if tokens[i][0] > a:span[3] | return s:ObjectSpan(a:span[2], i) | endif
  endfor
  return a:span[2] < line('$') ? s:ObjectSpan(a:span[2] + 1, 0) : []
endfunction

function! s:SpanPos(lnum, index) abort
  return [0, a:lnum, max([0, byteidx(getline(a:lnum), a:index)]) + 1, 0]
endfunction

function! pinyinvim#chinese#SelectObject(inner, context, count) abort
  let motion_count = v:count > 0 ? v:count : a:count
  if a:inner && motion_count == 1 && empty(getline('.')) | return | endif
  if !s:NeedsWords('w', motion_count)
    execute 'normal! ' . (a:context ==# 'x' ? '' : 'v') . motion_count . (a:inner ? 'iw' : 'aw')
    return
  endif
  let tokens = s:Tokens(line('.'))
  let at = s:At(tokens, s:Index())
  if at < 0 && !empty(tokens) | return | endif
  let first = s:ObjectSpan(line('.'), max([0, at]))
  let last = copy(first)
  for n in range(1, motion_count - 1)
    let next = s:NextObject(last)
    if empty(next) | break | endif
    let last = next
    " iw counts whitespace as an object, just like native Vim.
    if !a:inner && last[4] ==# 'space'
      let next = s:NextObject(last)
      if !empty(next) | let last = next | endif
      while !empty(next) && last[4] ==# 'space' && !empty(getline(last[0]))
        let next = s:NextObject(last)
        if !empty(next) | let last = next | endif
      endwhile
    endif
  endfor
  let start = s:SpanPos(first[0], first[1])
  let end = s:SpanPos(last[2], last[3])
  if !a:inner
    let next = s:NextObject(last)
    if first[4] ==# 'space' && !empty(next)
      let end = s:SpanPos(next[2], next[3])
    elseif !empty(next) && next[0] == last[2] && next[4] ==# 'space'
      let end = s:SpanPos(next[2], next[3])
    elseif at > 0 && tokens[at - 1][2] ==# 'space'
      let start = s:SpanPos(first[0], tokens[at - 1][0])
    endif
  endif
  let lnum = line('.')
  if a:context ==# 'x'
    let anchor = getpos('v')
    if anchor[1:2] ==# getpos('.')[1:2]
      execute "normal! \<Esc>"
      call s:Select(start, end)
    else
      call setpos('.', end)
    endif
  else
    call s:SelectWordRange(start, end, a:context)
  endif
endfunction

function! pinyinvim#chinese#Find(key, context) abort
  if !get(g:, 'pinyinvim_pinyin', 1) || !s:HasHan(getline('.'))
    let s:last_find = {}
    return a:key
  endif
  let motion_count = v:count1
  let g:pinyinvim_pending = (v:count ? string(v:count) : '') . a:key
  redrawstatus
  try
    " The default cursor='msg' moves the visible caret to the command line.
    let target = getcharstr(-1, {'cursor': 'keep'})
  finally
    let g:pinyinvim_pending = ''
    redrawstatus
  endtry
  " A find target is either one pinyin initial or one literal Unicode
  " character.  Literal punctuation (，。！？；：、 and their ASCII forms) is
  " intentionally supported; the old ASCII-only guard rejected it.
  let literal = strchars(target) == 1 && char2nr(target) >= 0x20
  if !((target =~# '^[a-z]$') || literal)
    if target !=# "\<Esc>" | let s:last_find = {} | endif
    return a:key . target
  endif
  if a:context ==# 'o' && s:FindIndex(a:key, target, motion_count, 0) < 0
    call setcharsearch({'char':target, 'forward':a:key ==# 'f' || a:key ==# 't', 'until':a:key ==# 't' || a:key ==# 'T'})
    let s:last_find = {'key':a:key, 'target':target, 'search':getcharsearch()}
    return "\<Esc>"
  endif
  return s:Call('MoveFind', [a:key, target, a:context, motion_count, 0])
endfunction

function! pinyinvim#chinese#RepeatFind(reverse, context) abort
  if empty(s:last_find) || !get(g:, 'pinyinvim_pinyin', 1) || getcharsearch() !=# s:last_find.search
    return a:reverse ? ',' : ';'
  endif
  let key = s:last_find.key
  if a:reverse | let key = key ==# 'f' ? 'F' : key ==# 'F' ? 'f' : key ==# 't' ? 'T' : 't' | endif
  return s:Call('MoveFind', [key, s:last_find.target, a:context, v:count1, 1])
endfunction

function! pinyinvim#chinese#MoveFind(key, target, context, count, repeated) abort
  let forward = a:key ==# 'f' || a:key ==# 't'
  let till = a:key ==# 't' || a:key ==# 'T'
  let dir = forward ? 1 : -1
  if !a:repeated
    call setcharsearch({'char':a:target, 'forward':forward, 'until':till})
    let s:last_find = {'key':a:key, 'target':a:target, 'search':getcharsearch()}
  endif
  let origin = getpos('.')
  let index = s:FindIndex(a:key, a:target, v:count > 0 ? v:count : a:count, a:repeated)
  if index < 0
    if a:context ==# 'o' && v:operator ==# 'c'
      call s:CancelEmptyChange()
    endif
    return
  endif
  let dest = [0, line('.'), byteidx(getline('.'), index) + 1, 0]
  if a:context ==# 'o'
    if !forward && dest ==# origin | return | endif
    " f/t are inclusive forwards; F/T are exclusive at the original cursor.
    call s:Select(forward ? origin : dest, forward ? dest : s:Previous(origin))
  else
    call setpos('.', dest)
  endif
endfunction

function! s:FindIndex(key, target, motion_count, repeated) abort
  let forward = a:key ==# 'f' || a:key ==# 't'
  let till = a:key ==# 't' || a:key ==# 'T'
  let dir = forward ? 1 : -1
  let chars = split(getline('.'), '\zs')
  " Literal characters always remain candidates. Lowercase letters also
  " match Han initials; a position is counted once, in document order.
  " Chinese and ASCII punctuation still match only their exact character.
  let initial_target = a:target =~# '^[a-z]$'
  let index = s:Index()
  let remaining = a:motion_count
  " Repeated t/T skips only the adjacent previous match, not every match.
  if a:repeated && till && a:motion_count == 1 | let index += dir | endif
  while remaining > 0
    let index += dir
    while index >= 0 && index < len(chars)
      " Vim groups a base character with its composing marks for cursor
      " offsets, while native f matches that base character itself.
      let base = strcharpart(chars[index], 0, 1)
      if base ==# a:target || (initial_target && stridx(get(s:initials, base, ''), a:target) >= 0) | break | endif
      let index += dir
    endwhile
    if index < 0 || index >= len(chars) | return -1 | endif
    let remaining -= 1
  endwhile
  if till | let index -= dir | endif
  return index
endfunction
