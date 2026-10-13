-- Prototype only. The draft stays a real Neovim buffer; these windows only
-- change presentation. No Vim motion/operator is reimplemented here.
local api, fn = vim.api, vim.fn
local M = { wins = {}, bufs = {}, message = '', pending = '', cmdlines = {}, history = {} }
local ns = api.nvim_create_namespace('pinyinvim.capsules')
local queued, started, timer = false, false, nil
local painted_feedback
local buttons = { 'mode', 'pending', 'settings', 'submit' }
local icons = { settings = '\u{e001}', submit = '\u{e002}' }
local blank_border = { ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ' }
local slim_border = { '', '', '', ' ', '', '', '', ' ' }

local function chunks_text(chunks)
  local out = {}
  for _, chunk in ipairs(chunks or {}) do out[#out + 1] = chunk[2] end
  return table.concat(out)
end

local function fit(text, width)
  text = text:gsub('[\r\n]+', ' ')
  if fn.strdisplaywidth(text) <= width then return text end
  local out = ''
  for _, char in ipairs(fn.split(text, '\\zs')) do
    if fn.strdisplaywidth(out .. char .. '…') > width then break end
    out = out .. char
  end
  return out .. '…'
end

local function scratch(name)
  local buf = api.nvim_create_buf(false, true)
  api.nvim_buf_set_name(buf, 'pinyinvim-capsule://' .. name)
  vim.bo[buf].bufhidden = 'hide'
  vim.bo[buf].swapfile = false
  return buf
end

-- Presets keep the shell and capsule surfaces related without letting a
-- background image overwrite the capsule colors. Values are intentionally
-- opaque base colors; the two transparency settings are applied separately.
local themes = {
  ['mist-blue'] = { label = '雾蓝', light = {
    outer='#C7D0DD', editor='#E4E9F1', message='#EDF1F7', controls='#D7E4EB',
    text='#263447', dim='#7C899A', cursorline='#D6DDE7', accent='#7A9DB8',
    hover='#C6DCE8', pressed='#B9CFDD', error='#9A3B36',
  }, dark = {
    outer='#252C38', editor='#303846', message='#36414E', controls='#394955',
    text='#E0E6EE', dim='#929FAF', cursorline='#3C4655', accent='#86A9C4',
    hover='#465B6A', pressed='#516575', error='#FFA59C',
  }},
  ['lemon'] = { label = '柠檬黄', light = {
    outer='#E9E2C1', editor='#FFF8D8', message='#FFFBEA', controls='#F0E4A2',
    text='#3C3928', dim='#827A52', cursorline='#F5EDB7', accent='#B38B16',
    hover='#F7E89A', pressed='#EED872', error='#A13A32',
  }, dark = {
    outer='#332F1F', editor='#403A24', message='#4A4328', controls='#514719',
    text='#F5EDC0', dim='#B8AC79', cursorline='#504726', accent='#D6B84A',
    hover='#6B5C2E', pressed='#796A37', error='#FFB0A0',
  }},
  ['lavender'] = { label = '薰衣草', light = {
    outer='#D8D0E3', editor='#EFE8F7', message='#F7F2FB', controls='#DFD0F1',
    text='#342D43', dim='#817793', cursorline='#E5DCF2', accent='#9876B7',
    hover='#E9DDF6', pressed='#D8C2EC', error='#A04462',
  }, dark = {
    outer='#302A3A', editor='#3B3348', message='#463B55', controls='#4A3B5E',
    text='#EEE7F7', dim='#B3A5C5', cursorline='#493E59', accent='#BE9ADB',
    hover='#614D78', pressed='#735C8C', error='#FF9EB7',
  }},
  ['mint'] = { label = '薄荷绿', light = {
    outer='#C8DED5', editor='#E4F1EC', message='#F1F8F4', controls='#CFE7DA',
    text='#243A33', dim='#6F8A7E', cursorline='#D6EBDF', accent='#5A9D7F',
    hover='#C2E2CF', pressed='#A9D2BA', error='#A13F47',
  }, dark = {
    outer='#24332E', editor='#2F4039', message='#384C43', controls='#355547',
    text='#E0F0E8', dim='#9BB3A7', cursorline='#3B5045', accent='#7BC49F',
    hover='#416A56', pressed='#4F7D65', error='#FF9B9B',
  }},
  ['peach'] = { label = '蜜桃', light = {
    outer='#E8D2C5', editor='#F7E8E0', message='#FFF3EE', controls='#F3D7C8',
    text='#432F29', dim='#967B70', cursorline='#F1DDD3', accent='#C98267',
    hover='#F0C9B5', pressed='#E7AF96', error='#A13D3D',
  }, dark = {
    outer='#392A27', editor='#47332E', message='#533C35', controls='#5C3D32',
    text='#F5E5DD', dim='#BDA197', cursorline='#554039', accent='#E0A184',
    hover='#785144', pressed='#8D5F50', error='#FFAAA0',
  }},
  ['rose'] = { label = '玫瑰', light = {
    outer='#E6CBD4', editor='#F5E3E8', message='#FFF1F4', controls='#EACDD7',
    text='#432B35', dim='#987784', cursorline='#F0D7DF', accent='#C36C88',
    hover='#F0CBD8', pressed='#E4AEC1', error='#A0334F',
  }, dark = {
    outer='#392831', editor='#49323E', message='#553A48', controls='#5B3849',
    text='#F6E4EB', dim='#C09BAA', cursorline='#563C48', accent='#E38EAA',
    hover='#77495D', pressed='#8C5870', error='#FF9CA9',
  }},
  ['sky'] = { label = '晴空蓝', light = {
    outer='#C5DCE9', editor='#E2F0F7', message='#F0F8FC', controls='#CBE5F1',
    text='#233746', dim='#708C9D', cursorline='#D3EAF4', accent='#4C91B7',
    hover='#BFE0EF', pressed='#9EC9DF', error='#A33F46',
  }, dark = {
    outer='#243541', editor='#2E4350', message='#374E5C', controls='#315365',
    text='#E0F0F7', dim='#9BB4C2', cursorline='#38505E', accent='#79C4E3',
    hover='#416D80', pressed='#50869C', error='#FF9FA0',
  }},
  ['sand'] = { label = '砂岩', light = {
    outer='#E4D9C6', editor='#F3EBDD', message='#FBF6EC', controls='#E9D6B8',
    text='#40372D', dim='#8F806F', cursorline='#EEE1CB', accent='#B9824E',
    hover='#EBD0A8', pressed='#DEB986', error='#A64238',
  }, dark = {
    outer='#332D25', editor='#43392D', message='#504335', controls='#55422D',
    text='#F2E5D0', dim='#B8A58D', cursorline='#514432', accent='#D9A66A',
    hover='#71583D', pressed='#876845', error='#FFA294',
  }},
  ['forest'] = { label = '森林', light = {
    outer='#C8D8C5', editor='#E2EEE0', message='#F0F7EE', controls='#CFE2C9',
    text='#293A2B', dim='#718873', cursorline='#D7E8D2', accent='#628F62',
    hover='#C7E0C4', pressed='#AED0AA', error='#A33D43',
  }, dark = {
    outer='#26332A', editor='#304235', message='#3A4D3D', controls='#3D5940',
    text='#E2F0E1', dim='#A2B9A2', cursorline='#3A5040', accent='#8FC48D',
    hover='#4A704D', pressed='#5C855E', error='#FFA0A0',
  }},
  ['graphite'] = { label = '石墨', light = {
    outer='#D1D4D8', editor='#E9EBEE', message='#F4F5F6', controls='#DCE1E5',
    text='#293038', dim='#717A84', cursorline='#E0E4E8', accent='#6E7F8F',
    hover='#D1D9E0', pressed='#BDC9D2', error='#A33E44',
  }, dark = {
    outer='#26292D', editor='#30343A', message='#383D43', controls='#3E454D',
    text='#E7EAED', dim='#9BA3AC', cursorline='#3A4047', accent='#AAB6C2',
    hover='#505A65', pressed='#626E7A', error='#FF9D9D',
  }},
}

local function shell_opacity()
  local background = vim.g.pinyinvim_background_transparency or 50
  -- The native backdrop is behind the Neovide swapchain.  Once a valid image
  -- is selected, the swapchain's default clear must be fully transparent so
  -- the image can actually reach the desktop.  With no image, retain the
  -- normal desktop transparency setting.
  local image = vim.g.pinyinvim_background_image or ''
  local has_image = image ~= '' and vim.fn.filereadable(image) == 1
  local opacity = has_image and 0 or (1 - background / 100)
  return math.max(0, math.min(1, opacity))
end

local function theme()
  local dark = vim.o.background == 'dark'
  local blend = vim.g.pinyinvim_capsule_transparency or 20
  local background = vim.g.pinyinvim_background_transparency or 50
  local preset = themes[vim.g.pinyinvim_theme or 'mist-blue'] or themes['mist-blue']
  local colors = preset[dark and 'dark' or 'light']
  local image = vim.g.pinyinvim_background_image or ''
  local has_image = image ~= '' and vim.fn.filereadable(image) == 1
  -- The native backdrop owns the desktop/image pixels. Neovide clears the
  -- entire swapchain with the default Normal style before drawing grid cells;
  -- its fractional trailing area therefore follows normal_opacity rather than
  -- a window highlight. Keep the shell RGB explicit and drive both paths from
  -- the same background transparency value.
  local shell_bg = has_image and 'NONE' or colors.outer
  local shell_blend = has_image and 100 or 0
  for name, bg in pairs({ Editor = colors.editor,
    Message = colors.message, Controls = colors.controls }) do
    api.nvim_set_hl(0, 'Capsule' .. name, { fg = colors.text, bg = bg, blend = blend })
  end
  -- Normal and CapsuleOuter deliberately share the shell RGB. Neovide's
  -- renderer recognises this as its default background and applies
  -- normal_opacity to both complete cells and the fractional clear remainder.
  api.nvim_set_hl(0, 'Normal', { fg = colors.text, bg = shell_bg, blend = shell_blend })
  api.nvim_set_hl(0, 'CapsuleOuter', { fg = colors.text, bg = shell_bg, blend = shell_blend })
  api.nvim_set_hl(0, 'CapsuleHint', { fg = colors.dim })
  api.nvim_set_hl(0, 'CapsuleDim', { fg = colors.dim, bg = colors.editor, blend = blend })
  api.nvim_set_hl(0, 'CapsuleLineNr', { fg = colors.dim, bg = colors.editor, blend = blend })
  api.nvim_set_hl(0, 'CapsuleCursorLine', { bg = colors.cursorline, blend = blend })
  api.nvim_set_hl(0, 'CapsuleError', { fg = colors.error, bg = colors.message, blend = blend })
  api.nvim_set_hl(0, 'CapsuleButton', { fg = colors.text, bg = colors.controls, blend = blend })
  for _, name in ipairs(buttons) do
    local active = M.pressed == name
    local hovered = M.hovered == name
    local bg = active and colors.pressed or hovered and colors.hover or colors.controls
    local group = 'Capsule' .. name:sub(1,1):upper() .. name:sub(2)
    api.nvim_set_hl(0, group, {fg = colors.text, bg = bg, blend = blend})
  end
  M.palette = colors
  M.render_revision = (M.render_revision or 0) + 1
end

function M.apply_appearance()
  -- normal_opacity is applied only to the renderer's default background; text
  -- and panels keep their own highlight alpha. This is the renderer-supported
  -- way to make the trailing clear area use the same backdrop as the shell.
  vim.g.neovide_normal_opacity = shell_opacity()
  vim.g.neovide_opacity = 1.0
  -- System materials and the rejected extra HostBackdropBrush are disabled.
  -- Keep the renderer's premultiplied transparency path unobstructed.
  vim.g.neovide_corner_preference = 'round'
  if started then theme(); M.render() end
end

function M.apply_settings(settings)
  M.pressed = nil
  vim.g.pinyinvim_glass = settings.glass == false and 0 or 1
  vim.g.pinyinvim_theme = settings.theme or 'mist-blue'
  vim.g.pinyinvim_background_transparency = settings.backgroundTransparency or 50
  vim.g.pinyinvim_capsule_transparency = settings.capsuleTransparency or 20
  vim.g.pinyinvim_blur_radius = settings.blurRadius or 5
  vim.g.pinyinvim_background_image = settings.backgroundImagePath or ''
  local actions = {
    hide = { modes = {'n'}, lhs = settings.hideNotation or '<Esc>', run = function() fn.PinyinHide() end },
    settings = { modes = {'n','i','x'}, lhs = settings.settingsNotation or '<F2>', run = function() fn.PinyinSettingsView() end },
    submit = { modes = {'n','i','x'}, lhs = settings.submitNotation or '<C-CR>', run = function() M.submit() end },
    copy = { modes = {'n','i','x'}, lhs = settings.copyNotation or '<C-c>', run = function() M.copy() end },
    paste = { modes = {'i'}, lhs = settings.pasteNotation or '<C-v>', run = function() M.paste() end },
  }
  if not M.bindings then
    -- Remove the bridge's initial defaults once, then own all local bindings.
    for _, pair in ipairs({{'n','<Esc>'},{'n','<F2>'},{'n','<C-CR>'},{'i','<C-CR>'},{'x','<C-CR>'}}) do
      pcall(vim.keymap.del, pair[1], pair[2])
    end
  end
  for _, old in pairs(M.bindings or {}) do
    for _, mode in ipairs(old.modes) do pcall(vim.keymap.del, mode, old.lhs) end
  end
  for _, action in pairs(actions) do
    vim.keymap.set(action.modes, action.lhs, action.run, {silent=true})
  end
  M.bindings = actions
  M.hide_label = settings.hideHotkey or 'Esc'
  M.settings_label = settings.settingsHotkey or 'F2'
  M.submit_label = settings.submitHotkey or 'Ctrl+Enter'
  M.apply_appearance()
end

local function mode_label()
  local mode = api.nvim_get_mode().mode
  if mode:sub(1, 1) == 'i' then return 'INSERT' end
  if mode:sub(1, 1) == 'R' then return 'REPLACE' end
  if mode == 'v' then return 'VISUAL' end
  if mode == 'V' then return 'V-LINE' end
  if mode == '\22' then return 'V-BLOCK' end
  if mode:sub(1, 1) == 'c' then return 'COMMAND' end
  if mode:sub(1, 1) == 's' then return 'SELECT' end
  return 'NORMAL'
end

function M.render()
  if not started then return end
  if M.hovered == 'submit' then M.hover_hint = M.submission_hint() end
  local pending = M.pending
  local chinese = vim.g.pinyinvim_pending or ''
  -- The Chinese mapping already exposes the complete count + find command.
  -- Native showcmd may still contain that count, so it must not be appended.
  if chinese ~= '' then
    pending = chinese
    if fn.mode(1):sub(1, 2) == 'no' and vim.v.operator ~= '' then
      pending = vim.v.operator .. pending
    end
  end
  local recording = fn.reg_recording()
  if recording ~= '' then pending = '@' .. recording end
  local texts = {mode = mode_label(), pending = fit(pending, 8), settings = icons.settings, submit = icons.submit}
  M.control_texts, M.hits = texts, {}
  for _, name in ipairs(buttons) do
    local width = api.nvim_win_get_width(M.wins[name])
    local text = texts[name]
    local padding = string.rep(' ', math.max(0, math.floor((width - fn.strdisplaywidth(text)) / 2)))
    local group = 'Capsule' .. name:sub(1,1):upper() .. name:sub(2)
    local cache = padding .. text .. ':' .. tostring(M.render_revision)
    M.text_cache = M.text_cache or {}
    M.hits[name] = math.floor(width / 2) + 1
    if M.text_cache[name] ~= cache then
      api.nvim_buf_clear_namespace(M.bufs[name], ns, 0, -1)
      api.nvim_buf_set_extmark(M.bufs[name], ns, 0, 0, {
        virt_text = {{padding .. text, group}}, virt_text_pos = 'overlay',
      })
      M.text_cache[name] = cache
    end
  end

  local message, level = M.message, 0
  for key in pairs(M.cmdlines) do level = math.max(level, key) end
  local cmd = M.cmdlines[level]
  if cmd then
    message = cmd.prefix .. cmd.text
  elseif (vim.g.pinyinvim_notice or '') ~= '' then
    message = vim.g.pinyinvim_notice
  end
  if not cmd and not M.message_error and (vim.g.pinyinvim_notice or '') == ''
    and M.hover_hint and M.hover_hint ~= '' then message = M.hover_hint end
  local msgwidth = api.nvim_win_get_width(M.wins.message)
  local display = fit(message, msgwidth)
  -- Keep the insertion position visible for long :commands and searches.
  if cmd and fn.strdisplaywidth(message) > msgwidth then
    local before = cmd.prefix .. cmd.text:sub(1, cmd.pos)
    local count = fn.strchars(before)
    local offset = 0
    while fn.strdisplaywidth('…' .. fn.strcharpart(before, offset)) >= msgwidth - 1 and offset < count do
      offset = offset + 1
    end
    display = fit('…' .. fn.strcharpart(message, offset), msgwidth)
    M.cmd_offset = fn.byteidx(message, offset) - #'…'
  else
    M.cmd_offset = 0
  end
  M.message_text = display
  api.nvim_buf_clear_namespace(M.bufs.message, ns, 0, -1)
  if cmd then
    local pos = math.min(#display, math.max(0, #cmd.prefix + cmd.pos - M.cmd_offset))
    api.nvim_buf_set_extmark(M.bufs.message, ns, 0, 0, {
      virt_text = { { display:sub(1, pos), 'CapsuleMessage' }, { '▏', 'CapsuleButton' },
        { display:sub(pos + 1), 'CapsuleMessage' } }, virt_text_pos = 'overlay',
    })
  else
    api.nvim_buf_set_extmark(M.bufs.message, ns, 0, 0, { virt_text = {
      { display, M.message_error and 'CapsuleError' or 'CapsuleMessage' } }, virt_text_pos = 'overlay' })
  end
  local empty = api.nvim_buf_line_count(M.draft) == 1
    and api.nvim_buf_get_lines(M.draft, 0, 1, false)[1] == ''
  -- An empty draft has no meaningful current line to emphasize. Keep the
  -- placeholder on the editor background; restore the user's original
  -- cursorline option as soon as actual text is present.
  vim.wo[M.wins.editor].cursorline = not empty and M.editor_options.cursorline or false
  local hint = empty and api.nvim_get_mode().mode == 'n'
    and api.nvim_win_get_buf(M.wins.editor) == M.draft
  local hint_key = tostring(hint) .. ':' .. (M.hide_label or 'Esc')
  if M.hint_key ~= hint_key then
    api.nvim_buf_clear_namespace(M.draft, ns, 0, -1)
    if hint then
      api.nvim_buf_set_extmark(M.draft, ns, 0, 0, {
        virt_text = {{'  ' .. (M.hide_label or 'Esc') .. ' 收起', 'CapsuleHint'}},
        virt_text_pos = 'overlay', hl_mode = 'combine',
      })
    end
    M.hint_key = hint_key
  end

end

local function render_feedback()
  if not started then return end
  M.render()
  -- Changes from getchar(), mode events and the host bridge all need to reach
  -- the UI. Compare presentation state so unchanged timer ticks stay idle.
  local feedback = table.concat({M.control_texts.pending, M.control_texts.mode,
    M.message_text or '', tostring(M.message_error), tostring(M.render_revision)}, '\0')
  if feedback ~= painted_feedback then
    painted_feedback = feedback
    api.nvim__redraw({ flush = true })
  end
end

function M.begin_session()
  -- Keep :messages history, but do not show an earlier session's error again.
  M.message, M.message_error = '', false
  render_feedback()
end

function M.submit()
  M.message, M.message_error = '', false
  vim.g.pinyinvim_notice = '正在提交…'
  render_feedback()
  fn.PinyinSubmit()
end

local function refresh()
  if queued then return end
  queued = true
  vim.schedule(function()
    queued = false
    render_feedback()
  end)
end

local function window(buf, name, config, focus)
  config.width, config.height = math.max(1, math.floor(config.width)), math.max(1, math.floor(config.height))
  config.relative = 'editor'
  if name ~= 'editor' then config.style = 'minimal' end
  config.border, config.zindex = name == 'editor' and blank_border or slim_border, 50
  config.focusable = focus or false
  config.mouse = name ~= 'message'
  config.noautocmd = true
  local win = M.wins[name]
  if win and api.nvim_win_is_valid(win) then
    api.nvim_win_set_config(win, config)
  else
    win = api.nvim_open_win(buf, focus or false, config)
    M.wins[name] = win
  end
  local group = 'Capsule' .. name:sub(1, 1):upper() .. name:sub(2)
  vim.wo[win].winhighlight = 'Normal:' .. group .. ',NormalFloat:' .. group .. ',FloatBorder:' .. group
    .. ',LineNr:CapsuleLineNr,CursorLine:CapsuleCursorLine,CursorLineNr:CapsuleLineNr'
  if name == 'editor' then
    for option, value in pairs(M.editor_options) do vim.wo[win][option] = value end
  else
    vim.wo[win].wrap = false
    vim.wo[win].signcolumn = 'no'
    vim.wo[win].foldcolumn = '0'
    vim.wo[win].fillchars = 'eob: '
    vim.wo[win].number = false
    vim.wo[win].cursorline = false
  end
end

function M.layout()
  if not started then return end
  local cols, rows = vim.o.columns, vim.o.lines
  -- One physical spacing unit for ALL margins/gaps, including the vertical
  -- ones. The adapter keeps fractional anchors; only content sizes use cells.
  local geometry = M.geometry or {}
  local ratio = geometry.gap_rows or 0.42
  local margin_y, margin_x, gap = ratio, 1, 1
  local remainder = 0
  if geometry.cell_width then
    cols = math.floor((geometry.width - 1) / geometry.cell_width + .01)
    remainder = geometry.width / geometry.cell_width - cols
    margin_x = 1 + remainder / 2
    gap = 1 + remainder / 3
    ratio = (geometry.height - (geometry.editor_rows + 3) * geometry.cell_height) / (3 * geometry.cell_height)
    margin_y = ratio
  end
  local total = math.max(43, cols - 2)
  -- Four fixed-width controls: long mode names and 8 pending cells never move
  -- the action icons. Only the message and editor gain space on resize.
  gap = 1 + remainder / 6
  local widths = {mode=9, pending=8, settings=3, submit=3}
  local left = total - 1 - 31 - 3 -- integral grid width; subpixel remainder stays in gaps
  local editor_height = M.geometry and M.geometry.editor_rows or math.max(1, rows - 4)
  local footer_row = editor_height + 2 + 2 * ratio
  window(M.draft, 'editor', { row = margin_y, col = margin_x, width = math.max(1, total - 2),
    height = math.max(1, editor_height) }, true)
  window(M.bufs.message, 'message', { row = footer_row, col = gap,
    width = math.max(1, left - 2), height = 1 })
  local col = gap + left + gap
  for _, name in ipairs(buttons) do
    window(M.bufs[name], name, {row=footer_row, col=col, width=widths[name], height=1})
    col = col + widths[name] + 2 + gap
  end
  M.render()
  if fn.exists('*PinyinCapsuleMetrics') == 1 then
    local bounds = {}
    for name, win in pairs(M.wins) do
      local c = api.nvim_win_get_config(win)
      bounds[name] = { left = c.col, top = c.row, right = c.col + c.width + 2,
        bottom = c.row + c.height + (name == 'editor' and 2 or 0) }
    end
    fn.PinyinCapsuleMetrics(cols, rows, vim.g.neovide_scale_factor or 1, vim.o.guifont, bounds, geometry.revision or 0)
  end
end

function M.on_geometry(geometry)
  M.geometry = geometry
  -- Borderless drag intentionally keeps focus unchanged. Avoid Neovide's
  -- unfocused 30 Hz deadline while a pointer gesture is in progress.
  vim.g.neovide_refresh_rate_idle = geometry.dragging and 60 or 30
  local scale = math.floor(geometry.zoom * 1000 + 0.5) / 1000
  if math.abs((vim.g.neovide_scale_factor or 1) - scale) > 0.002 then
    vim.g.neovide_scale_factor = scale
  end
  M.layout()
end

function M.copy()
  local visual = false
  local ok, err = pcall(function()
    local mode = api.nvim_get_mode().mode
    if mode == 'v' or mode == 'V' or mode == '\22' then
      visual = true
      vim.cmd.normal({ args = { '"+y' }, bang = true })
      vim.cmd.normal({ args = { 'gv' }, bang = true })
    else
      fn.setreg('+', table.concat(api.nvim_buf_get_lines(M.draft, 0, -1, false), '\n'), 'v')
    end
  end)
  if ok and not visual and fn.exists('*PinyinClipboardCopied') == 1 then fn.PinyinClipboardCopied() end
  if not ok then api.nvim_echo({ { '复制失败：' .. tostring(err), 'ErrorMsg' } }, true, { err = true }) end
end

function M.paste()
  local ok, err = pcall(function()
    api.nvim_paste(fn.getreg('+'), true, -1)
  end)
  if not ok then api.nvim_echo({ { '粘贴失败：' .. tostring(err), 'ErrorMsg' } }, true, { err = true }) end
end

local function ui_event(event, ...)
  if event == 'msg_showmode' or event == 'msg_ruler' then return true end
  if event:sub(1, 4) ~= 'msg_' and event:sub(1, 8) ~= 'cmdline_' then return end
  local args = { ... }
  if event == 'msg_showcmd' then
    M.pending = chunks_text(args[1])
    -- Native count/operator waits can delay scheduled callbacks until the
    -- next key. This event normally runs in safe context and can paint now.
    if vim.in_fast_event() then vim.schedule(render_feedback) else render_feedback() end
    return true
  end
  -- Mark messages handled before deferring API mutations out of fast context.
  vim.schedule(function()
    if not started then return end
    if event == 'msg_show' then
      local text = chunks_text(args[2])
      M.message = args[5] and (M.message .. text) or text
      M.message_error = args[1] == 'emsg' or args[1] == 'lua_error' or args[1] == 'echoerr'
      M.history[#M.history + 1] = M.message
      if #M.history > 100 then table.remove(M.history, 1) end
    elseif event == 'msg_clear' then
      M.message, M.message_error = '', false
    elseif event == 'msg_history_show' then
      local messages = {}
      for _, entry in ipairs(args[1] or {}) do messages[#messages + 1] = chunks_text(entry[2]) end
      M.message = table.concat(messages, ' · ')
    elseif event == 'cmdline_show' then
      M.cmdlines[args[6]] = { text = chunks_text(args[1]), pos = args[2],
        prefix = args[3] .. args[4] .. string.rep(' ', args[5]) }
    elseif event == 'cmdline_pos' then
      if M.cmdlines[args[2]] then M.cmdlines[args[2]].pos = args[1] end
    elseif event == 'cmdline_hide' then
      M.cmdlines[args[1]] = nil
    elseif event == 'cmdline_special_char' then
      local cmd = M.cmdlines[args[3]]
      if cmd then
        cmd.text = cmd.text:sub(1, cmd.pos) .. args[1] .. cmd.text:sub(cmd.pos + 1)
      end
    elseif event == 'cmdline_block_show' then
      M.message = chunks_text(args[1][#args[1]])
    elseif event == 'cmdline_block_append' then
      M.message = chunks_text(args[1])
    elseif event == 'cmdline_block_hide' then
      M.message = ''
    end
    M.render()
    api.nvim__redraw({ flush = true })
  end)
  return true
end

local function mouse_panel()
  local mouse = fn.getmousepos()
  for _, name in ipairs(buttons) do
    if mouse.winid == M.wins[name] then return name end
  end
end

function M.submission_hint()
  return (vim.g.pinyinvim_route == '粘贴提交' and '粘贴到原输入框' or '仅复制到剪贴板') .. ' · ' .. (M.submit_label or 'Ctrl+Enter')
end

function M.set_hover(name)
  if name == '' then name = nil end
  if M.hovered ~= name then
    M.hovered = name
    local hints = {mode='当前编辑模式', pending='等待后续按键',
      settings='设置 · ' .. (M.settings_label or 'F2'),
      submit=M.submission_hint()}
    M.hover_hint = hints[name]
    theme(); M.render()
  end
  return ''
end

function M.click()
  local name = mouse_panel()
  if name then
    M.set_hover(name)
    M.pressed = name; theme(); M.render()
    return ''
  end
  if fn.getmousepos().winid == M.root then return '' end
  return api.nvim_replace_termcodes('<LeftMouse>', true, false, true)
end

function M.release()
  local pressed = M.pressed
  M.pressed = nil
  if not pressed then return api.nvim_replace_termcodes('<LeftRelease>', true, false, true) end
  local name = mouse_panel()
  theme(); M.render()
  if name == pressed and M.hovered == pressed and (name == 'settings' or name == 'submit') then
    vim.schedule(function()
      api.nvim_set_current_win(M.wins.editor)
      if name == 'settings' then fn.PinyinSettingsView() else M.submit() end
      refresh()
    end)
  end
  return ''
end

function M.drag()
  if M.pressed then return '' end
  return api.nvim_replace_termcodes('<LeftDrag>', true, false, true)
end

function M.inspect()
  local result = { mode = mode_label(), raw_mode = fn.mode(1), operator = vim.v.operator,
    chinese_pending = vim.g.pinyinvim_pending, pending = M.pending, message = M.message, wins = {}, draft = M.draft,
    current = api.nvim_get_current_win(), root = M.root, editor = M.wins.editor }
  for name, win in pairs(M.wins) do
    result.wins[name] = { config = api.nvim_win_get_config(win), buf = api.nvim_win_get_buf(win),
      lines = name == 'editor' and api.nvim_buf_get_lines(api.nvim_win_get_buf(win), 0, -1, false)
        or { name == 'message' and M.message_text or (M.control_texts[name] or '') } }
  end
  return result
end

function M.start()
  if started then return end
  -- Neovide can leave the unnamed buffer current even after loading the
  -- command-line file. Reuse the host's named draft buffer, not that transient
  -- current buffer; bufadd/bufload preserve an already loaded draft's contents.
  local draft = vim.env.PINYINVIM_DRAFT
  M.draft = draft and draft ~= '' and fn.bufadd(draft) or api.nvim_get_current_buf()
  fn.bufload(M.draft)
  started = true
  M.root = api.nvim_get_current_win()
  M.editor_options = {}
  for _, option in ipairs({ 'number', 'relativenumber', 'numberwidth', 'cursorline', 'wrap',
    'signcolumn', 'foldcolumn', 'fillchars', 'list', 'spell', 'scrolloff', 'sidescrolloff' }) do
    M.editor_options[option] = vim.wo[M.root][option]
  end
  M.bufs.outer, M.bufs.message = scratch('outer'), scratch('message')
  for _, name in ipairs(buttons) do M.bufs[name] = scratch(name) end
  api.nvim_win_set_buf(M.root, M.bufs.outer)
  vim.wo[M.root].number, vim.wo[M.root].cursorline = false, false
  vim.wo[M.root].winhighlight = 'Normal:CapsuleOuter,EndOfBuffer:CapsuleOuter'
  vim.wo[M.root].fillchars = 'eob: '
  theme()
  M.layout()
  vim.ui_attach(ns, { ext_messages = true, set_cmdheight = false }, ui_event)
  local group = api.nvim_create_augroup('PinyinCapsules', { clear = true })
  api.nvim_create_autocmd('VimResized', { group = group, callback = M.layout })
  api.nvim_create_autocmd('WinEnter', { group = group, callback = function()
    if api.nvim_get_current_win() == M.root and api.nvim_win_is_valid(M.wins.editor) then
      api.nvim_set_current_win(M.wins.editor)
    end
  end })
  api.nvim_set_current_win(M.wins.editor)
  vim.cmd.redraw()
  api.nvim_create_autocmd({ 'ModeChanged', 'TextChanged', 'TextChangedI', 'BufEnter' }, { group = group, callback = refresh })
  api.nvim_create_autocmd('OptionSet', { group = group, pattern = 'background', callback = function() theme(); refresh() end })
  for _, mode in ipairs({ 'n', 'i', 'x', 'o' }) do
    for key, action in pairs({['<LeftMouse>']=M.click, ['<LeftRelease>']=M.release,
      ['<LeftDrag>']=M.drag}) do
      vim.keymap.set(mode, key, action, {expr=true, replace_keycodes=false, silent=true})
    end
  end
  -- The Chinese f/t mapping waits in getchar() and exposes its pending key as
  -- a global. A small timer reads that state; it never consumes or adds keys.
  timer = vim.uv.new_timer()
  timer:start(80, 80, vim.schedule_wrap(render_feedback))
  api.nvim_create_autocmd('VimLeavePre', { group = group, callback = function()
    started = false
    timer:stop(); timer:close()
    vim.ui_detach(ns)
  end })
end

function M.setup()
  vim.o.laststatus, vim.o.cmdheight, vim.o.showtabline = 0, 0, 0
  vim.o.showcmd, vim.o.showmode, vim.o.ruler, vim.o.more = true, false, false, false
  vim.o.showcmdloc = 'last'
  vim.o.linespace = 6
  vim.g.neovide_window_blurred = false -- no system material under the DirectComposition surface
  vim.o.mousemoveevent = false -- native policy reports panel transitions, including same-cell grid changes
  M.apply_appearance()
  vim.g.neovide_refresh_rate = 120
  vim.g.neovide_refresh_rate_idle = 30
  vim.g.neovide_scale_factor = 1.0
  -- The presentation adapter keeps fractional float anchors. Neovim 0.12's
  -- global mouse detector uses integer compositor positions, which can put a
  -- visible footer click in the root instead. Let Neovide detect the rendered
  -- grid and send local coordinates; M.click uses that same coordinate model.
  vim.g.neovide_has_mouse_grid_detection = false
  vim.g.neovide_floating_corner_radius = 1.0
  vim.g.neovide_floating_shadow = false
  vim.g.neovide_floating_blur_amount_x, vim.g.neovide_floating_blur_amount_y = 0, 0
  vim.g.neovide_padding_top, vim.g.neovide_padding_bottom = 0, 0
  vim.g.neovide_padding_left, vim.g.neovide_padding_right = 0, 0
  vim.g.neovide_cursor_animation_length = 0.03
  -- Geometry changes must not animate old float bounds across the new frame.
  -- Cursor/scroll animations remain independent from window resizing.
  vim.g.neovide_position_animation_length = 0
  for _, mode in ipairs({ 'n', 'i', 'x' }) do
    vim.keymap.set(mode, '<C-ScrollWheelUp>', function() fn.PinyinCapsuleAdjustFont(1) end, { silent = true })
    vim.keymap.set(mode, '<C-ScrollWheelDown>', function() fn.PinyinCapsuleAdjustFont(-1) end, { silent = true })
  end
  if vim.env.PINYINVIM_CAPSULE_RPC then fn.serverstart(vim.env.PINYINVIM_CAPSULE_RPC) end
  -- UIEnter can precede command-line file loading in an embedded startup.
  -- Capturing the buffer then binds the editor to an unnamed buffer instead
  -- of the real draft and bypasses the existing persistence checks.
  local function start_when_ready()
    vim.schedule(function()
      if vim.v.vim_did_enter == 1 and #api.nvim_list_uis() > 0 then M.start() end
    end)
  end
  api.nvim_create_autocmd({ 'UIEnter', 'VimEnter' }, { callback = start_when_ready })
  start_when_ready()
end

return M
