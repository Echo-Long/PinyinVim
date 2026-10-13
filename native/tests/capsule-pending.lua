-- Exercise real Neovim input and the redraw grid delivered to a UI client.
-- Reading only capsule.inspect() misses extmarks that never reach the screen.
local uv, fn = vim.uv, vim.fn
local fixture = fn.tempname() .. '-capsule-startup'
fn.mkdir(fixture, 'p')
local config, draft = fixture .. '/init.vim', fixture .. '/draft.txt'
fn.writefile({'今天 review 中文，明天 merge 项目。'}, draft)
fn.writefile({'set rtp^=' .. fn.fnameescape(fn.getcwd() .. '/native/nvim'),
  'runtime plugin/chinese.vim', 'lua vim.env.PINYINVIM_DRAFT=' .. string.format('%q',draft),
  -- Reproduce Neovide's loaded draft with an unnamed buffer left current.
  'lua vim.api.nvim_create_autocmd("VimEnter",{callback=function() vim.cmd.enew() end})',
  'lua _G.caps=dofile(' .. string.format('%q',
    fn.getcwd() .. '/native/prototypes/capsules/lua/pinyinvim/capsules.lua') .. '); caps.setup()'}, config)
local stdin, stdout, stderr = uv.new_pipe(false), uv.new_pipe(false), uv.new_pipe(false)
local responses, serial, exited, pending_win, grid = {}, 0, false, nil, nil
local cells, errors, float_grids, message_cells = {}, {}, {}, {}
local message_win, message_grid, message_width
local child = assert(uv.spawn(vim.v.progpath, {
  args={'--embed','-n','-u',config,'-i','NONE',draft}, cwd=fn.getcwd(), stdio={stdin,stdout,stderr},
}, function() exited=true end))
local unpacker = vim.mpack.Unpacker()
stdout:read_start(function(err, data)
  assert(not err, err)
  if not data then return end
  local pos=1
  while pos<=#data do
    local item; item,pos=unpacker(data,pos)
    if not item then break end
    if item[1]==1 then responses[item[2]]=item end
    if item[1]==2 and item[2]=='redraw' then
      for _,event in ipairs(item[3]) do
        for i=2,#event do
          local arg=event[i]
          -- Window handles in UI events can be MessagePack extension payloads.
          if event[1]=='win_float_pos' then
            float_grids[arg[2]]=arg[1]
            if pending_win and (arg[2]==pending_win or arg[2]==vim.mpack.encode(pending_win)) then grid=arg[1] end
            if message_win and (arg[2]==message_win or arg[2]==vim.mpack.encode(message_win)) then message_grid=arg[1] end
          end
          if event[1]=='grid_clear' and arg[1]==grid then cells={} end
          if event[1]=='grid_line' and arg[1]==grid and arg[2]==0 then
            local col=arg[3]+1
            for _,cell in ipairs(arg[4]) do
              for _=1,(cell[3] or 1) do cells[col]=cell[1]; col=col+1 end
            end
          end
          if event[1]=='grid_clear' and arg[1]==message_grid then message_cells={} end
          if event[1]=='grid_line' and arg[1]==message_grid and arg[2]==0 then
            local col=arg[3]+1
            for _,cell in ipairs(arg[4]) do
              for _=1,(cell[3] or 1) do message_cells[col]=cell[1]; col=col+1 end
            end
          end
        end
      end
    end
  end
end)
stderr:read_start(function(_,data) if data then errors[#errors+1]=data end end)
local function rpc(method,...)
  serial=serial+1; local id=serial
  stdin:write(vim.mpack.encode({0,id,method,{...}}))
  assert(vim.wait(4000,function() return responses[id]~=nil or exited end,10),'RPC timed out: '..method)
  assert(responses[id],'Child exited: '..method..' '..table.concat(errors))
  local response=responses[id]; responses[id]=nil
  if response[3]~=vim.NIL then error(vim.inspect(response[3])) end
  return response[4]
end
local function lua(code,...) return rpc('nvim_exec_lua',code,{...}) end
local function input(keys) rpc('nvim_input',keys) end
local function displayed()
  local text={}
  for col=1,10 do text[col]=cells[col] or ' ' end
  return (table.concat(text):gsub('^%s+',''):gsub('%s+$',''))
end
local checks=0
local function pending(text,label)
  assert(vim.wait(2000,function() return displayed()==text end,10),
    label..': expected '..vim.inspect(text)..', received '..vim.inspect(displayed()))
  checks=checks+1
end
local function message_contains(text,label)
  assert(vim.wait(2000,function()
    local text_cells={}; for col=1,message_width do text_cells[col]=message_cells[col] or ' ' end
    return table.concat(text_cells):find(text,1,true)~=nil
  end,10),label)
  checks=checks+1
end
local ok,err=xpcall(function()
  rpc('nvim_ui_attach',100,30,{ext_linegrid=true,ext_multigrid=true})
  assert(vim.wait(3000,function() return lua('return _G.caps ~= nil and caps.wins.pending ~= nil') end,20),
    'capsules must start after VimEnter and UI attachment')
  assert(lua([[local path=...; return vim.api.nvim_buf_get_name(caps.draft):gsub('\\','/'):lower()
    ==vim.fn.fnamemodify(path,':p'):gsub('\\','/'):lower()]],draft),'real command-line draft owns the editor buffer')
  checks=checks+1
  assert(lua('return vim.api.nvim_buf_get_lines(caps.draft,0,-1,false)[1]') == '今天 review 中文，明天 merge 项目。',
    'startup preserves the loaded draft despite an unnamed current buffer')
  checks=checks+1
  pending_win=lua('return caps.wins.pending')
  grid=float_grids[pending_win] or float_grids[vim.mpack.encode(pending_win)]
  message_win=lua('return caps.wins.message')
  message_grid=float_grids[message_win] or float_grids[vim.mpack.encode(message_win)]
  message_width=lua('return vim.api.nvim_win_get_width(caps.wins.message)')

  input('2'); pending('2','one count digit paints before the next key')
  input('f'); pending('2f','count is not duplicated by Chinese find')
  input('m'); pending('','completed find clears the capsule')
  assert(lua('return vim.api.nvim_win_get_cursor(caps.wins.editor)[2]')==30,
    '2fm must still reach the second match, the m in merge')
  checks=checks+1

  for _,key in ipairs({'f','F','t','T'}) do
    input('0'..key); pending(key,'Chinese '..key..' paints while waiting')
    input('\27'); pending('','cancel '..key..' clears the painted capsule')
  end
  input('1'); pending('1','first digit of a longer count')
  input('2'); pending('12','second digit extends the count once')
  input('f'); pending('12f','long count is not duplicated')
  input('\27'); pending('','cancel counted find')

  input('d'); pending('d','native operator paints immediately')
  input('2'); pending('d2','native motion count paints immediately')
  input('f'); pending('d2f','operator and Chinese wait reach the UI grid')
  input('\27'); pending('','cancel operator find')
  for _,case in ipairs({{'2df','d2f'},{'2d3f','d6f'},{'"ad2f','d2f'},{'cf','cf'},{'yf','yf'}}) do
    input('0'..case[1]); pending(case[2],case[1]..' preserves its effective operator/count')
    input('\27'); pending('','cancel '..case[1])
  end

  lua([[vim.api.nvim_buf_set_lines(caps.draft,0,-1,false,{'alpha beta'});
    vim.api.nvim_win_set_cursor(caps.wins.editor,{1,0})]])
  input('f'); pending('f','native ASCII find still paints')
  input('l'); pending('','native ASCII find completion')
  assert(lua('return vim.api.nvim_win_get_cursor(caps.wins.editor)[2]')==1,'native fl still reaches l')
  checks=checks+1
  input('g'); pending('g','native multi-key command paints')
  input('g'); pending('','native multi-key completion clears')

  input('i<C-a>'); message_contains('E29','native last-insert error reaches the UI')
  lua('caps.begin_session()')
  assert(lua('return caps.message == "" and not caps.message_error'),'new session clears an earlier error')
  checks=checks+1
  lua([[vim.cmd([=[function! PinyinSubmit() abort
    let g:submit_calls=get(g:,'submit_calls',0)+1
  endfunction
  function! PinyinHide() abort
  endfunction]=]); caps.apply_settings({})]])
  input('<C-CR>'); message_contains('正在提交','submit shortcut paints acceptance without asking for key-up')
  assert(lua('return vim.g.submit_calls')==1,'submit shortcut retains the existing submit function')
  checks=checks+1
  lua([[vim.g.pinyinvim_notice='已发送粘贴']])
  message_contains('已发送粘贴','host notice paints without a pending-key change')
end,function(e) return debug.traceback(vim.inspect(e),2) end)
pcall(rpc,'nvim_input','\27')
pcall(rpc,'nvim_command','qa!')
vim.wait(1000,function() return exited end,10)
if not exited then child:kill('sigterm') end
stdin:close(); stdout:close(); stderr:close(); child:close()
if not ok then error(err) end
fn.writefile({vim.json.encode({checks=checks,failures={}})},'native/tests/capsule-pending.json')
