-- ded_bridge.lua
-- Sends a JSON payload over UDP at ~10 Hz containing all cockpit params
-- plus computed/converted values for common display fields.
-- Chains into existing DCS export callbacks so other scripts keep working.

local socket = require("socket")
local udp = nil
local host, port = "127.0.0.1", 7778
local nextSend = 0
local sendHz = 10
local sendPeriod = 1.0 / sendHz

local function jescape(s)
  if s == nil then return "" end
  s = tostring(s)
  s = s:gsub("\\", "\\\\"):gsub('"', '\\"')
  return s
end

local function appendField(parts, k, v)
  if type(v) == "number" then
    table.insert(parts, string.format('"%s":%.6g', jescape(k), v))
  else
    table.insert(parts, string.format('"%s":"%s"', jescape(k), jescape(v)))
  end
end

----------------------------------------------------------------
-- Parse helpers for DCS cockpit data
----------------------------------------------------------------

local function parseCockpitParams()
  local result = {}
  local ok, params = pcall(list_cockpit_params)
  if ok and params then
    for line in params:gmatch("[^\n]+") do
      local key, val = line:match("^([^:]+):(.+)$")
      if key then
        val = val:match('^"(.*)"$') or val
        result[key] = val
      end
    end
  end
  return result
end

local function parseIndication(id, fieldName)
  local ok, text = pcall(list_indication, id)
  if not ok or not text then return nil end
  local capture = false
  for line in text:gmatch("[^\n]+") do
    if capture then return line end
    if line == fieldName then capture = true end
  end
  return nil
end

----------------------------------------------------------------
-- Save any previously-defined callbacks so we can chain them
----------------------------------------------------------------
local _prevLuaExportStart               = LuaExportStart
local _prevLuaExportStop                = LuaExportStop
local _prevLuaExportActivityNextEvent   = LuaExportActivityNextEvent

----------------------------------------------------------------
-- Our logic
----------------------------------------------------------------
local function dedBridgeStart()
  udp = socket.udp()
  udp:settimeout(0)
  udp:setpeername(host, port)
  log.write("DED_BRIDGE", log.INFO, "UDP socket opened to " .. host .. ":" .. port)
end

local function dedBridgeStop()
  if udp then udp:close() end
  udp = nil
  log.write("DED_BRIDGE", log.INFO, "UDP socket closed")
end

local function dedBridgeSend(t)
  if t < nextSend then return end
  nextSend = t + sendPeriod

  local selfData = LoGetSelfData()
  if not selfData then return end

  local cp = parseCockpitParams()

  local altASL = LoGetAltitudeAboveSeaLevel() or 0
  local hdg    = LoGetMagneticYaw() or 0

  -- Computed/converted values for default display fields
  local hdg_deg   = (hdg * 57.2958) % 360
  local alt_ft    = altASL * 3.28084

  local uhf_mhz   = tonumber(cp["UHF_FREQ"])    or 0
  local vhfam_mhz = tonumber(cp["VHF_AM_FREQ"]) or 0
  local vhffm_mhz = tonumber(cp["VHF_FREQ"])    or 0

  -- VHF FM fallback to device 56 (ARC-186 FM) if cockpit param is 0
  if vhffm_mhz == 0 then
    local ok, freq = pcall(function() return GetDevice(56):get_frequency() end)
    if ok and freq and freq > 0 then vhffm_mhz = freq / 1000000 end
  end

  -- Fuel kg -> lbs
  local fuel_kg  = tonumber(cp["BASE_SENSOR_FUEL_TOTAL"]) or 0
  local fuel_lbs = fuel_kg * 2.20462

  local stpt_name = cp["STEERPOINT"] or ""
  local stpt_num  = parseIndication(3, "CurrSteerPointNumber") or "0"

  -- IAS m/s -> knots
  local ias_mps = tonumber(cp["BASE_SENSOR_IAS"]) or 0
  local ias_kt  = ias_mps * 1.94384

  -- ── Build JSON payload ────────────────────────────────────────
  local parts = {}

  -- Named/computed fields (keys used by default FieldDefinitions)
  appendField(parts, "name",      selfData.Name or "")
  appendField(parts, "hdg_deg",   hdg_deg)
  appendField(parts, "alt_ft",    alt_ft)
  appendField(parts, "ias_kt",    ias_kt)
  appendField(parts, "uhf_mhz",   uhf_mhz)
  appendField(parts, "vhfam_mhz", vhfam_mhz)
  appendField(parts, "vhffm_mhz", vhffm_mhz)
  appendField(parts, "fuel_lbs",  fuel_lbs)
  appendField(parts, "stpt_num",  stpt_num)
  appendField(parts, "stpt_name", stpt_name)
  appendField(parts, "t",         t)

  -- All raw cockpit params (BASE_SENSOR_*, UHF_FREQ, STEERPOINT, etc.)
  for k, v in pairs(cp) do
    local num = tonumber(v)
    if num then
      appendField(parts, k, num)
    else
      appendField(parts, k, v)
    end
  end

  local msg = "{" .. table.concat(parts, ",") .. "}"
  if udp then udp:send(msg) end
end

----------------------------------------------------------------
-- Chained global callbacks
----------------------------------------------------------------
LuaExportStart = function()
  dedBridgeStart()
  if _prevLuaExportStart then _prevLuaExportStart() end
end

LuaExportStop = function()
  dedBridgeStop()
  if _prevLuaExportStop then _prevLuaExportStop() end
end

LuaExportActivityNextEvent = function(t)
  dedBridgeSend(t)
  local tNext = t + sendPeriod
  if _prevLuaExportActivityNextEvent then
    local tPrev = _prevLuaExportActivityNextEvent(t)
    if tPrev and tPrev < tNext then tNext = tPrev end
  end
  return tNext
end
