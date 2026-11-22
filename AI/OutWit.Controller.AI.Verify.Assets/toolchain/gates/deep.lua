-- One deep C stack per run, with one protected call at its bottom, so the error unwinds
-- the whole stack at once. Usage: lua deep.lua <scenario>
local scenario = arg[1]

local scenarios = {}

scenarios.index = function()
  local function deep(n)
    return setmetatable({}, { __index = function(_, k) return deep(n + 1)[k] end })
  end
  return deep(0).x
end

scenarios.gsub = function()
  local function g(s) return (s:gsub(".", function(c) return g(c) end)) end
  return g("ab")
end

scenarios.tostring = function()
  local mt = {}
  mt.__tostring = function(t) return "<" .. tostring(setmetatable({}, mt)) .. ">" end
  return tostring(setmetatable({}, mt))
end

scenarios.concat = function()
  local mt = {}
  mt.__concat = function(a, b) return setmetatable({}, mt) .. "x" end
  return setmetatable({}, mt) .. "y"
end

scenarios.format = function()
  local mt = {}
  mt.__tostring = function() return string.format("%s", setmetatable({}, mt)) end
  return string.format("%s", setmetatable({}, mt))
end

scenarios.sortcmp = function()
  local function nested(n)
    local t = { 3, 2, 1 }
    table.sort(t, function(a, b) nested(n + 1); return a < b end)
  end
  nested(0)
end

scenarios.parse = function()
  local s = string.rep("(", 1000) .. "1" .. string.rep(")", 1000)
  local f, e = load("return " .. s)
  error(e)
end

scenarios.parsetable = function()
  local s = string.rep("{", 1000) .. string.rep("}", 1000)
  local f, e = load("return " .. s)
  error(e)
end

scenarios.load = function()
  local function deep(n)
    return load(function() deep(n + 1); return nil end)
  end
  deep(0)
end

scenarios.call = function()
  -- Lua-to-C-to-Lua: pcall is not used inside, the error rises through every level.
  local function f(n) return select(2, string.gsub("a", "a", function() return f(n + 1) end)) end
  return f(0)
end

scenarios.memory = function()
  local function deep(n)
    return setmetatable({}, { __index = function(_, k)
      if n == 150 then return string.rep("x", 1 << 30) end
      return deep(n + 1)[k]
    end })
  end
  return deep(0).x
end

local ok, e = pcall(scenarios[scenario])
print(scenario, ok, (tostring(e):match("[^:]+$")))
