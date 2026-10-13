-- Exercise real Neovim input and the redraw grid delivered to a UI client.
-- Reading only capsule.inspect() misses extmarks that never reach the screen.
local uv, fn = vim.uv, vim.fn
local stdin, stdout, stderr = uv.new_pipe(false), uv.new_pipe(false), uv.new_pipe(false)
local responses, serial, exited, pending_win, grid = {}, 0, false, nil, nil
local cells, errors, float_grids = {}, {}, {}
local child = assert(uv.spawn(vim.v.progpath, {
  args={'--embed','-n','-u','NONE','-i','NONE'}, cwd=fn.getcwd(), stdio={stdin,stdout,stderr},
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
          end
          if event[1]=='grid_clear' and arg[1]==grid then cells={} end
          if event[1]=='grid_line' and arg[1]==grid and arg[2]==0 then
            local col=arg[3]+1
            for _,cell in ipairs(arg[4]) do
              for _=1,(cell[3] or 1) do cells[col]=cell[1]; col=col+1 end
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
local ok,err=xpcall(function()
  rpc('nvim_ui_attach',100,30,{ext_linegrid=true,ext_multigrid=true})
  rpc('nvim_command','set rtp^=native/nvim')
  rpc('nvim_command','runtime plugin/chinese.vim')
  lua([[_G.caps=dofile('native/prototypes/capsules/lua/pinyinvim/capsules.lua'); caps.setup(); caps.start();
    vim.api.nvim_buf_set_lines(caps.draft,0,-1,false,{'今天 review 中文，明天 merge 项目。'});
    vim.api.nvim_win_set_cursor(caps.wins.editor,{1,0}); vim.cmd.redraw()]])
  pending_win=lua('return caps.wins.pending')
  grid=float_grids[pending_win] or float_grids[vim.mpack.encode(pending_win)]

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
end,function(e) return debug.traceback(vim.inspect(e),2) end)
pcall(rpc,'nvim_input','\27')
pcall(rpc,'nvim_command','qa!')
vim.wait(1000,function() return exited end,10)
if not exited then child:kill('sigterm') end
stdin:close(); stdout:close(); stderr:close(); child:close()
if not ok then error(err) end
fn.writefile({vim.json.encode({checks=checks,failures={}})},'native/tests/capsule-pending.json')
