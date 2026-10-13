if exists('g:loaded_pinyinvim_nvim_bridge') | finish | endif
let g:loaded_pinyinvim_nvim_bridge = 1
scriptencoding utf-8

let s:channel = -1
let s:session = 0
let s:rx = ''
let s:next_request = 0
let s:pending = {}
let s:settings_view = 0
let s:draft_bufnr = -1
let s:settings_bufnr = -1
let s:settings = {}
let s:quit_requested = 0
let g:pinyinvim_clear_on_close = 0
let g:pinyinvim_route = '剪贴板提交'
let g:pinyinvim_notice = ''

function! PinyinMode() abort
  let l:m = mode()
  return get({'n':'NORMAL','i':'INSERT','v':'VISUAL','V':'V-LINE',"\<C-V>":'V-BLOCK','c':'COMMAND','R':'REPLACE','s':'SELECT'}, l:m, toupper(l:m))
endfunction

function! PinyinActivity() abort
  let l:pending = get(g:, 'pinyinvim_pending', '')
  if !empty(l:pending)
    return l:pending . ' · 输入中文、标点或拼音首字母 '
  endif
  let l:notice = get(g:, 'pinyinvim_notice', '')
  if !empty(l:notice)
    return l:notice . ' '
  endif
  let l:register = reg_recording()
  return empty(l:register) ? '' : '录制 @' . l:register . ' '
endfunction

function! s:RequestId(kind) abort
  let s:next_request += 1
  let l:id = printf('%s-%d', a:kind, s:next_request)
  let s:pending[l:id] = a:kind
  return l:id
endfunction

function! s:ChannelOpen() abort
  if s:channel <= 0 | return 0 | endif
  " Vim exposes ch_status(), but Neovim 0.12 does not.  Keep the check
  " conditional so the same bridge can run in both runtimes.  On Neovim a
  " positive sockconnect() id is usable until chansend() reports a failure.
  if exists('*ch_status')
    try
      return ch_status(s:channel) ==# 'open'
    catch
      let s:channel = -1
      return 0
    endtry
  endif
  return 1
endfunction

function! s:DropChannel() abort
  if s:channel > 0
    try
      call chanclose(s:channel)
    catch
    endtry
  endif
  let s:channel = -1
endfunction

function! s:Send(message, ...) abort
  if !s:ChannelOpen() | return '' | endif
  let l:message = deepcopy(a:message)
  let l:needs_ack = a:0 > 0 ? a:1 : 0
  if l:needs_ack
    let l:message.requestId = s:RequestId(get(l:message, 'kind', 'request'))
  endif
  let l:message.token = $PINYINVIM_TOKEN
  let l:message.session = s:session
  try
    let l:sent = chansend(s:channel, json_encode(l:message) . "\n")
    if type(l:sent) == v:t_number && l:sent < 0
      call s:DropChannel()
      let g:pinyinvim_notice = '桥接连接已断开'
      redrawstatus
      return ''
    endif
  catch
    call s:DropChannel()
    let g:pinyinvim_notice = '桥接连接已断开'
    redrawstatus
    return ''
  endtry
  return get(l:message, 'requestId', '')
endfunction

function! s:IsDraft() abort
  return !empty($PINYINVIM_DRAFT)
        \ && substitute(expand('%:p'), '\\', '/', 'g') ==? substitute(fnamemodify($PINYINVIM_DRAFT, ':p'), '\\', '/', 'g')
endfunction

function! s:PersistDraft() abort
  if !s:IsDraft() || !&modified | return | endif
  let l:lines = getline(1, '$')
  if len(l:lines) == 1 && l:lines[0] ==# '' | let l:lines = [] | endif
  call pinyinvim#draft#Save($PINYINVIM_DRAFT, l:lines)
  setlocal nomodified
endfunction

function! s:ClearDraft() abort
  if !s:IsDraft() | return | endif
  " Persist first: a failed write must not discard the only usable copy.
  call pinyinvim#draft#Save($PINYINVIM_DRAFT, [])
  silent %delete _
  setlocal nomodified
endfunction

function! s:ApplySettings(settings) abort
  let s:settings = deepcopy(a:settings)
  let g:pinyinvim_pinyin = get(a:settings, 'pinyin', 1)
  let g:pinyinvim_words = get(a:settings, 'words', 1)
  let g:pinyinvim_clear_on_close = get(a:settings, 'clearOnClose', 0)
  let l:size = max([10, min([28, get(a:settings, 'fontSize', 14)])])
  execute 'set guifont=Consolas,等距更纱黑体\ SC:h' . l:size
  if get(a:settings, 'dark', 0)
    set background=dark
  else
    set background=light
  endif
  call s:SyncNeovideIme(PinyinMode())
  redrawstatus
  if s:settings_view | call s:RenderSettings() | endif
endfunction

function! s:SetNeovideIme(enabled) abort
  " Neovide owns the actual Windows IME input path.  Keep this assignment
  " behind g:neovide so the same runtime remains usable in headless tests and
  " ordinary Neovim sessions.  The try/catch also covers Neovide startup
  " windows whose RPC channel is not ready yet; the next mode event retries it.
  if !exists('g:neovide') | return | endif
  " Disabling the automatic policy leaves manual IME input available.
  let l:enabled = !get(s:settings, 'ime', 1) || a:enabled
  try
    let g:neovide_input_ime = l:enabled ? v:true : v:false
  catch
    let g:pinyinvim_notice = '输入法切换暂不可用'
  endtry
endfunction

function! s:SyncNeovideIme(mode) abort
  let l:enabled = a:mode ==# 'INSERT' || a:mode ==# 'REPLACE' || a:mode ==# 'COMMAND'
  call s:SetNeovideIme(l:enabled)
endfunction

function! s:RenderSettings() abort
  if !s:settings_view | return | endif
  setlocal modifiable
  silent! %delete _
  call setline(1, [
        \ '拼音浮笺  设置',
        \ '',
        \ '同一窗口设置。按数字切换，+ / - 调整字号，q、Enter 或 Esc 返回。',
        \ '',
        \ '[1] 拼音首字母定位        ' . (get(s:settings, 'pinyin', 1) ? '开' : '关'),
        \ '[2] 中文分词导览          ' . (get(s:settings, 'words', 1) ? '开' : '关'),
        \ '[3] 自动切换中英文        ' . (get(s:settings, 'ime', 1) ? '开' : '关'),
        \ '[4] 关闭时清空草稿        ' . (get(s:settings, 'clearOnClose', 0) ? '开' : '关'),
        \ '[5] 深色主题              ' . (get(s:settings, 'dark', 0) ? '开' : '关'),
        \ '[6] 编辑字号              ' . get(s:settings, 'fontSize', 14),
        \ '',
        \ '当前草稿默认保留。编辑器的 Normal / Insert / Visual 语义不改变。'])
  setlocal nomodified nomodifiable
  redraw!
endfunction

function! PinyinSettingsView() abort
  if s:settings_view | return | endif
  let s:draft_bufnr = bufnr('%')
  " enew can reuse an empty draft buffer, which made Back() return to the
  " settings content itself.  Allocate a real second buffer, then switch the
  " same window to it; this is still one Neovide window and never a split.
  let s:settings_bufnr = bufadd('[PinyinVim Settings]')
  call bufload(s:settings_bufnr)
  execute 'buffer ' . s:settings_bufnr
  let s:settings_view = 1
  let b:pinyinvim_settings = 1
  setlocal buftype=nofile bufhidden=wipe noswapfile
  nnoremap <silent> <buffer> 1 :call <SID>ToggleSetting('pinyin')<CR>
  nnoremap <silent> <buffer> 2 :call <SID>ToggleSetting('words')<CR>
  nnoremap <silent> <buffer> 3 :call <SID>ToggleSetting('ime')<CR>
  nnoremap <silent> <buffer> 4 :call <SID>ToggleSetting('clearOnClose')<CR>
  nnoremap <silent> <buffer> 5 :call <SID>ToggleSetting('dark')<CR>
  nnoremap <silent> <buffer> 6 :call <SID>AdjustFont(1)<CR>
  nnoremap <silent> <buffer> + :call <SID>AdjustFont(1)<CR>
  nnoremap <silent> <buffer> - :call <SID>AdjustFont(-1)<CR>
  nnoremap <silent> <buffer> q :call PinyinSettingsBack()<CR>
  nnoremap <silent> <buffer> <CR> :call PinyinSettingsBack()<CR>
  nnoremap <silent> <buffer> <Esc> :call PinyinSettingsBack()<CR>
  call s:RenderSettings()
endfunction

function! s:ToggleSetting(key) abort
  if !s:settings_view | return | endif
  let s:settings[a:key] = !get(s:settings, a:key, 0)
  call s:Send({'kind': 'settings-update', 'settings': s:settings}, 1)
  call s:RenderSettings()
endfunction

function! s:AdjustFont(delta) abort
  if !s:settings_view | return | endif
  let s:settings.fontSize = max([10, min([28, get(s:settings, 'fontSize', 14) + a:delta])])
  call s:Send({'kind': 'settings-update', 'settings': s:settings}, 1)
  call s:RenderSettings()
endfunction

function! PinyinSettingsBack() abort
  if !s:settings_view | return | endif
  let l:settings_buf = s:settings_bufnr
  let s:settings_view = 0
  if bufexists(s:draft_bufnr)
    execute 'buffer ' . s:draft_bufnr
  else
    enew!
  endif
  if l:settings_buf > 0 && bufexists(l:settings_buf) && l:settings_buf != bufnr('%')
    execute 'silent! bwipeout! ' . l:settings_buf
  endif
  let s:settings_bufnr = -1
  redraw!
endfunction

function! s:EmitMode() abort
  let l:mode = PinyinMode()
  call s:SyncNeovideIme(l:mode)
  call s:Send({'kind': 'mode-change', 'mode': l:mode, 'recording': reg_recording()})
endfunction

function! s:Handle(message) abort
  if get(a:message, 'token', '') !=# $PINYINVIM_TOKEN | return | endif
  let l:kind = get(a:message, 'kind', '')
  if l:kind ==# 'session'
    if s:settings_view | call PinyinSettingsBack() | endif
    let s:session = get(a:message, 'session', 0)
    let g:pinyinvim_route = get(a:message, 'routeLabel', get(a:message, 'paste', 0) ? '粘贴提交' : '剪贴板提交')
    let g:pinyinvim_notice = ''
    call s:ApplySettings(get(a:message, 'settings', {}))
    if get(a:message, 'clear', 0) && s:IsDraft()
      " Do not discard edits made while an earlier paste was in flight.
      if !has_key(a:message, 'clearHash') || sha256(join(getline(1, '$'), "\n")) ==# a:message.clearHash
        call s:ClearDraft()
      endif
    endif
    call s:SyncNeovideIme(PinyinMode())
    call s:Send({'kind':'session-ready'}, 1)
    call s:EmitMode()
  elseif l:kind ==# 'settings'
    call s:ApplySettings(get(a:message, 'settings', {}))
  elseif l:kind ==# 'settings-view'
    call PinyinSettingsView()
  elseif l:kind ==# 'route-update'
    let g:pinyinvim_route = get(a:message, 'routeLabel', g:pinyinvim_route)
    redrawstatus
  elseif l:kind ==# 'session-ready-ack' || l:kind ==# 'hide-ack' || l:kind ==# 'submit-ack' || l:kind ==# 'settings-ack' || l:kind ==# 'ime-ack'
    if get(a:message, 'session', s:session) != s:session | return | endif
    if get(a:message, 'ok', 0)
      if l:kind ==# 'submit-ack'
        let l:reason = get(a:message, 'reason', '')
        let g:pinyinvim_notice = l:reason ==# 'paste-sent' ? '已发送粘贴'
              \ : l:reason ==# 'copied-fallback' ? '已复制，请手动粘贴' : '已复制到剪贴板'
      else
        let g:pinyinvim_notice = ''
      endif
    elseif get(a:message, 'reason', '') ==# 'empty-draft'
      let g:pinyinvim_notice = '草稿为空'
    else
      let g:pinyinvim_notice = '操作失败：' . get(a:message, 'reason', 'unknown')
    endif
    let l:req = get(a:message, 'requestId', '')
    if has_key(s:pending, l:req) | call remove(s:pending, l:req) | endif
    redrawstatus
  elseif l:kind ==# 'quit'
    let s:quit_requested = 1
    let g:pinyinvim_clear_on_close = get(a:message, 'clearOnClose', get(g:, 'pinyinvim_clear_on_close', 0))
    if s:IsDraft() && get(g:, 'pinyinvim_clear_on_close', 0)
      call s:ClearDraft()
    else
      call s:PersistDraft()
    endif
    silent! qall!
  endif
endfunction

function! s:OnData(chan, data, event) abort
  " Neovim on_data callbacks provide a readfile-style list: every item
  " except the last one ends at a newline boundary, while the first/last
  " item may be partial.  Joining without restoring those boundaries leaves
  " incoming JSON buffered forever when the peer sends one line at a time.
  for l:index in range(len(a:data))
    let s:rx .= a:data[l:index]
    if l:index < len(a:data) - 1
      let s:rx .= "\n"
    endif
  endfor
  while stridx(s:rx, "\n") >= 0
    let l:at = stridx(s:rx, "\n")
    let l:line = strpart(s:rx, 0, l:at)
    let s:rx = strpart(s:rx, l:at + 1)
    if !empty(l:line)
      try
        call s:Handle(json_decode(l:line))
      catch
        let g:pinyinvim_notice = '桥接消息解析失败'
        redrawstatus
      endtry
    endif
  endwhile
endfunction

function! s:Connect() abort
  if empty($PINYINVIM_PORT) | return | endif
  if s:ChannelOpen() | return | endif
  let s:channel = sockconnect('tcp', '127.0.0.1:' . $PINYINVIM_PORT, {'on_data': function('s:OnData')})
  if s:channel > 0
    call s:Send({'kind':'ready','hwnd':0,'pid':getpid()})
  endif
endfunction

function! s:OnUIEnter() abort
  " The live bridge must be established from the GUI lifecycle.  An eager
  " connection during config loading can report ready before Neovide has
  " attached its UI and makes a UIEnter smoke test meaningless.
  call s:Connect()
  call s:SyncNeovideIme(PinyinMode())
endfunction

function! PinyinClipboardCopied() abort
  call s:Send({'kind':'clipboard-copied'})
endfunction

function! PinyinSubmit() abort
  if s:settings_view | return | endif
  call s:PersistDraft()
  call s:Send({'kind':'submit-request','text':join(getline(1,'$'),"\n")}, 1)
endfunction

function! PinyinHide() abort
  call s:PersistDraft()
  call s:Send({'kind':'hide-request'}, 1)
endfunction

function! PinyinSettingsClick(minwid, clicks, button, modifiers) abort
  if a:button ==# 'l' | call PinyinSettingsView() | endif
  return 0
endfunction

function! PinyinSubmitClick(minwid, clicks, button, modifiers) abort
  if a:button ==# 'l' | call PinyinSubmit() | endif
  return 0
endfunction

function! s:OnVimLeave() abort
  call s:Leaving()
  call s:Send({'kind':'exiting'})
endfunction

function! s:Leaving() abort
  if s:IsDraft()
    if s:quit_requested && get(g:, 'pinyinvim_clear_on_close', 0)
      call s:ClearDraft()
    else
      call s:PersistDraft()
    endif
  endif
endfunction

command! PinyinSubmit call PinyinSubmit()
command! PinyinSettings call PinyinSettingsView()
command! PinyinHide call PinyinHide()
nnoremap <silent> <Esc> <Cmd>call PinyinHide()<CR>
nnoremap <silent> <F2> <Cmd>call PinyinSettingsView()<CR>
nnoremap <silent> <C-CR> <Cmd>call PinyinSubmit()<CR>
inoremap <silent> <C-CR> <Cmd>call PinyinSubmit()<CR>
xnoremap <silent> <C-CR> <Cmd>call PinyinSubmit()<CR>

augroup PinyinNativeNvimBridge
  autocmd!
  autocmd UIEnter * call <SID>OnUIEnter()
  autocmd InsertEnter * call s:SetNeovideIme(1) | call s:Send({'kind':'ime-enter'}, 1)
  autocmd InsertLeavePre * call s:SetNeovideIme(0) | call s:Send({'kind':'ime-leave','mode':'NORMAL'}, 1)
  autocmd CmdlineEnter * call s:SetNeovideIme(1) | call s:Send({'kind':'ime-enter','mode':'COMMAND'}, 1)
  autocmd CmdlineLeavePre * call s:SetNeovideIme(0) | call s:Send({'kind':'ime-leave','mode':'NORMAL'}, 1)
  autocmd ModeChanged * call s:EmitMode()
  autocmd CursorMoved,CursorMovedI * call s:EmitMode()
  autocmd TextYankPost * if index(['+', '*'], get(v:event, 'regname', '')) >= 0 | call PinyinClipboardCopied() | endif
  autocmd TextChanged,TextChangedI,InsertLeave * call s:PersistDraft()
  autocmd VimLeavePre * call s:OnVimLeave()
augroup END
" Keep a config-time fallback for Neovim builds that delay UIEnter while
" retaining the UIEnter retry used by the live Neovide lifecycle.
call s:Connect()
set laststatus=2
set mouse=a
let &statusline = '%#StatusLine# %{PinyinActivity()}%S%=%{PinyinMode()}  │  %@PinyinSettingsClick@⚙%T  %@PinyinSubmitClick@↵%T '
