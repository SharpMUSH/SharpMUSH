-- The interface a SharpMUSH game offers Mudlet when its staff have not set one of their own
-- (+config mudlet-client/gui-url). Everything it shows comes from the GMCP packages the MUD-kit sends:
--
--   Room.Info, Room.Players                    the room panel and the map      (gmcp-room)
--   Char.Vitals                                the gauges                      (gmcp-char)
--   Char.Items.List, Add, Remove               things here and things carried  (gmcp-items)
--   Comm.Channel.Text                          the chat window                 (gmcp-comm)
--
-- A game that wants its own interface can start from this one: download it from the game's
-- /mudlet/sharpmush-ui.mpackage, change it in Mudlet, export it under a new name, and set gui-url.

SharpMUSH = SharpMUSH or {}
local ui = SharpMUSH

ui.package = "sharpmush-ui"
ui.settingsFile = getMudletHomeDir() .. "/sharpmush-ui.settings.lua"
ui.width = 0.3

ui.colors = {
  panel = "#14181f",
  border = "#2c3440",
  heading = { 230, 236, 245 },
  text = { 196, 204, 214 },
  muted = { 128, 138, 152 },
  accent = { 94, 214, 196 },
  gauges = { "#c0392b", "#2e86de", "#27ae60", "#d4a017", "#8e44ad", "#16a085" },
}

-- ── Settings kept between sessions ────────────────────────────────────────

function ui.loadSettings()
  ui.settings = { hidden = false }
  if io.exists(ui.settingsFile) then
    local saved = {}
    table.load(ui.settingsFile, saved)
    ui.settings.hidden = saved.hidden == true
  end
end

function ui.saveSettings()
  table.save(ui.settingsFile, ui.settings)
end

-- ── Text ──────────────────────────────────────────────────────────────────

local function rgb(color)
  return string.format("<%d,%d,%d>", color[1], color[2], color[3])
end

-- decho reads <r,g,b> as a colour; a name the game sends must not be taken for one.
local function plain(text)
  return (tostring(text or ""):gsub("<(%d+,%d+,%d+)>", "‹%1›"))
end

local function names(list)
  local out = {}
  for _, entry in ipairs(list or {}) do
    out[#out + 1] = plain(entry.name or entry.fullname or "")
  end
  return out
end

-- ── Layout ────────────────────────────────────────────────────────────────

local panelStyle = [[
  background-color: %s;
  border-left: 1px solid %s;
]]

function ui.build()
  if ui.dock then
    return
  end

  ui.dock = Geyser.Container:new({
    name = "sharpmush.dock", x = "-30%", y = 0, width = "30%", height = "100%",
  })

  ui.back = Geyser.Label:new({
    name = "sharpmush.back", x = 0, y = 0, width = "100%", height = "100%",
  }, ui.dock)
  ui.back:setStyleSheet(string.format(panelStyle, ui.colors.panel, ui.colors.border))

  ui.room = Geyser.MiniConsole:new({
    name = "sharpmush.room", x = 8, y = 8, width = "100%-16", height = "28%-8",
    autoWrap = true, color = ui.colors.panel, fontSize = 10,
  }, ui.dock)

  ui.gauges = Geyser.Container:new({
    name = "sharpmush.gauges", x = 8, y = "28%", width = "100%-16", height = "10%",
  }, ui.dock)

  ui.chat = Geyser.MiniConsole:new({
    name = "sharpmush.chat", x = 8, y = "38%+6", width = "100%-16", height = "32%-12",
    autoWrap = true, color = ui.colors.panel, fontSize = 10, scrollBar = true,
  }, ui.dock)

  ui.map = Geyser.Mapper:new({
    name = "sharpmush.map", x = 8, y = "70%", width = "100%-16", height = "30%-8",
  }, ui.dock)

  ui.gaugeList = {}
  ui.resize()
  ui.drawRoom()
  ui.drawVitals()
  ui.chat:decho(rgb(ui.colors.muted) .. "Channel lines and pages appear here.\n")
end

function ui.resize()
  if not ui.dock then
    return
  end
  if ui.settings.hidden then
    ui.dock:hide()
    setBorderRight(0)
  else
    ui.dock:show()
    local width = getMainWindowSize()
    setBorderRight(math.floor(width * ui.width))
  end
end

-- ── Room, people and things ───────────────────────────────────────────────

ui.items = { inv = {}, room = {} }

function ui.drawRoom()
  if not ui.room then
    return
  end
  ui.room:clear()
  local info = gmcp.Room and gmcp.Room.Info
  if not info then
    ui.room:decho(rgb(ui.colors.muted) .. "Where you are appears here once you are in the game.\n")
    return
  end

  ui.room:decho(rgb(ui.colors.heading) .. plain(info.name) .. "\n")
  if info.area and info.area ~= "" then
    ui.room:decho(rgb(ui.colors.muted) .. plain(info.area) .. "\n")
  end

  local exits = {}
  for name in pairs(info.exits or {}) do
    exits[#exits + 1] = plain(name)
  end
  table.sort(exits)
  ui.line("Exits", exits)

  local here = names(gmcp.Room.Players)
  ui.line("Here", here)
  ui.line("Things", names(ui.items.room))
  ui.line("Carrying", names(ui.items.inv))
end

function ui.line(label, list)
  if #list == 0 then
    return
  end
  ui.room:decho(rgb(ui.colors.accent) .. label .. ": " .. rgb(ui.colors.text) .. table.concat(list, ", ") .. "\n")
end

function ui.onItemsList()
  local list = gmcp.Char.Items.List
  if list and ui.items[list.location] then
    ui.items[list.location] = list.items or {}
    ui.drawRoom()
  end
end

function ui.onItemsAdd()
  local change = gmcp.Char.Items.Add
  if change and ui.items[change.location] and change.item then
    table.insert(ui.items[change.location], change.item)
    ui.drawRoom()
  end
end

function ui.onItemsRemove()
  local change = gmcp.Char.Items.Remove
  if not (change and ui.items[change.location] and change.item) then
    return
  end
  local list = ui.items[change.location]
  for index, item in ipairs(list) do
    if tostring(item.id) == tostring(change.item.id) then
      table.remove(list, index)
      break
    end
  end
  ui.drawRoom()
end

-- ── Gauges: one for each value sent with a max<value> beside it ───────────

function ui.drawVitals()
  if not ui.gauges then
    return
  end
  local vitals = (gmcp.Char and gmcp.Char.Vitals) or {}
  local keys = {}
  for key, value in pairs(vitals) do
    local max = vitals["max" .. key]
    if not key:find("^max") and tonumber(value) and tonumber(max) then
      keys[#keys + 1] = key
    end
  end
  table.sort(keys)

  local wanted = table.concat(keys, " ")
  if wanted ~= ui.gaugeKeys then
    for _, gauge in pairs(ui.gaugeList) do
      gauge:hide()
    end
    ui.gaugeList = {}
    ui.gaugeKeys = wanted
    local count = math.max(#keys, 1)
    for index, key in ipairs(keys) do
      local gauge = Geyser.Gauge:new({
        name = "sharpmush.gauge." .. key,
        x = 0, y = string.format("%d%%", math.floor((index - 1) * 100 / count)),
        width = "100%", height = string.format("%d%%-3", math.floor(100 / count)),
      }, ui.gauges)
      local color = ui.colors.gauges[(index - 1) % #ui.colors.gauges + 1]
      gauge.front:setStyleSheet("background-color: " .. color .. "; border-radius: 3px;")
      gauge.back:setStyleSheet("background-color: #232a34; border-radius: 3px;")
      ui.gaugeList[key] = gauge
    end
  end

  for key, gauge in pairs(ui.gaugeList) do
    local value, max = tonumber(vitals[key]), tonumber(vitals["max" .. key])
    gauge:setValue(math.max(math.min(value, max), 0), math.max(max, 1),
      string.format("<center>%s %s / %s</center>", key:upper(), value, max))
  end
end

-- ── Chat ──────────────────────────────────────────────────────────────────

function ui.onChannelText()
  local line = gmcp.Comm.Channel.Text
  if not (ui.chat and line and line.text) then
    return
  end
  ui.chat:decho(ansi2decho(line.text) .. "\n")
end

-- ── Map: placed by direction as you walk, or where the game says ──────────

ui.directions = {
  n = { "n", 0, 1, 0 }, north = { "n", 0, 1, 0 },
  s = { "s", 0, -1, 0 }, south = { "s", 0, -1, 0 },
  e = { "e", 1, 0, 0 }, east = { "e", 1, 0, 0 },
  w = { "w", -1, 0, 0 }, west = { "w", -1, 0, 0 },
  ne = { "ne", 1, 1, 0 }, northeast = { "ne", 1, 1, 0 },
  nw = { "nw", -1, 1, 0 }, northwest = { "nw", -1, 1, 0 },
  se = { "se", 1, -1, 0 }, southeast = { "se", 1, -1, 0 },
  sw = { "sw", -1, -1, 0 }, southwest = { "sw", -1, -1, 0 },
  u = { "up", 0, 0, 1 }, up = { "up", 0, 0, 1 },
  d = { "down", 0, 0, -1 }, down = { "down", 0, 0, -1 },
}

-- An exit's name as the game shows it, such as "North" or "North <N>": its first word.
function ui.direction(name)
  local word = tostring(name):lower():match("^%s*([%a]+)")
  return word and ui.directions[word]
end

function ui.area(name)
  local area = (name and name ~= "") and name or "SharpMUSH"
  local id = getAreaTable()[area]
  if not id then
    id = addAreaName(area)
  end
  return id
end

function ui.place(id, area, x, y, z)
  addRoom(id)
  setRoomArea(id, area)
  setRoomCoordinates(id, x, y, z)
end

function ui.mapRoom()
  local info = gmcp.Room and gmcp.Room.Info
  local id = info and tonumber(info.num)
  if not id then
    return
  end
  local area = ui.area(info.area)
  local coords = info.coords and { tostring(info.coords):match("^(%-?%d+),(%-?%d+),(%-?%d+)$") }

  if not roomExists(id) then
    local x, y, z = 0, 0, 0
    if ui.lastRoom and roomExists(ui.lastRoom) then
      x, y, z = getRoomCoordinates(ui.lastRoom)
      x = x + 2
    end
    ui.place(id, area, x, y, z)
  end
  if coords and coords[1] then
    setRoomCoordinates(id, tonumber(coords[1]), tonumber(coords[2]), tonumber(coords[3]))
  end
  setRoomName(id, info.name or "")

  local x, y, z = getRoomCoordinates(id)
  for name, target in pairs(info.exits or {}) do
    local to = tonumber(target)
    if to then
      local direction = ui.direction(name)
      if not roomExists(to) then
        if direction then
          ui.place(to, area, x + direction[2], y + direction[3], z + direction[4])
        else
          ui.place(to, area, x + 1, y - 1, z)
        end
      end
      if direction then
        setExit(id, to, direction[1])
      else
        addSpecialExit(id, to, tostring(name))
      end
    end
  end

  ui.lastRoom = id
  centerview(id)
end

function ui.onRoomInfo()
  ui.mapRoom()
  ui.drawRoom()
end

-- ── The interface command ─────────────────────────────────────────────────

function ui.command(word)
  word = (word or ""):lower()
  if word == "hide" then
    ui.settings.hidden = true
    ui.saveSettings()
    ui.resize()
    cecho("<cyan>The game's interface is hidden. <white>interface show<cyan> brings it back.\n")
  elseif word == "show" then
    ui.settings.hidden = false
    ui.saveSettings()
    ui.resize()
    cecho("<cyan>The game's interface is shown.\n")
  else
    cecho("<cyan>The game's interface shows the room, gauges, chat and a map from what the game sends.\n")
    cecho("<white>interface hide<cyan> takes it away, <white>interface show<cyan> brings it back.\n")
  end
end

-- ── Wiring ────────────────────────────────────────────────────────────────

-- Mudlet lists Room, Char and Char.Items when it connects, but not Comm: without this the game sends
-- no chat to a client that listed what it wants.
function ui.announce()
  sendGMCP([[Core.Supports.Add ["Comm.Channel 1"] ]])
  sendGMCP("Char.Items.Inv")
  sendGMCP("Char.Items.Room")
end

function ui.unregister()
  for _, id in ipairs(ui.handlers or {}) do
    killAnonymousEventHandler(id)
  end
  ui.handlers = {}
  if ui.alias then
    killAlias(ui.alias)
    ui.alias = nil
  end
end

function ui.register()
  ui.unregister()
  local function on(event, handler)
    ui.handlers[#ui.handlers + 1] = registerAnonymousEventHandler(event, handler)
  end
  on("gmcp.Room.Info", ui.onRoomInfo)
  on("gmcp.Room.Players", ui.drawRoom)
  on("gmcp.Char.Vitals", ui.drawVitals)
  on("gmcp.Char.Items.List", ui.onItemsList)
  on("gmcp.Char.Items.Add", ui.onItemsAdd)
  on("gmcp.Char.Items.Remove", ui.onItemsRemove)
  on("gmcp.Comm.Channel.Text", ui.onChannelText)
  on("sysWindowResizeEvent", ui.resize)
  on("sysProtocolEnabled", function(_, protocol)
    if protocol == "GMCP" then
      ui.announce()
    end
  end)
  on("sysUninstallPackage", function(_, name)
    if name == ui.package then
      ui.remove()
    end
  end)
  ui.alias = tempAlias("^interface(?: (\\w+))?$", function()
    ui.command(matches[2])
  end)
end

function ui.remove()
  ui.unregister()
  if ui.dock then
    ui.dock:hide()
  end
  setBorderRight(0)
end

ui.loadSettings()
ui.register()
ui.build()
ui.announce()
