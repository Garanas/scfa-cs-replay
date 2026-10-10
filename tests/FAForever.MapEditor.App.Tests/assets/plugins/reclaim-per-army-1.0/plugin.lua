-- Reclaim per army: the mass and energy of the props each player is likely to reclaim first.
-- Every prop counts for the spawn nearest to it. A command reports the totals, a check warns when
-- an army has much more or less than the average, and the panel sets how much is too much.
-- An example of the editor's Lua API (see the map editor's guide, "Lua plugins").

plugin.field{
  key = "tolerance", label = "Tolerance (%)", type = "number", default = 25, min = 0, max = 100,
  help = "How far an army's reclaim may be from the average before the check warns",
}

-- The reclaim nearest to each spawn: a list of { name, ref, mass, energy, props }.
local function perArmy()
  local spawns = map.markers("spawn")
  local totals = {}
  for i, spawn in ipairs(spawns) do
    totals[i] = { name = spawn.name, ref = spawn.ref, mass = 0, energy = 0, props = 0 }
  end
  if #spawns == 0 then
    return totals
  end
  local values = {}
  for _, prop in ipairs(map.props()) do
    local value = values[prop.blueprint]
    if value == nil then
      local mass, energy = map.reclaim(prop.blueprint)
      value = { mass = mass, energy = energy }
      values[prop.blueprint] = value
    end
    local nearest, distance = 1, math.huge
    for i, spawn in ipairs(spawns) do
      local dx, dz = prop.x - spawn.x, prop.z - spawn.z
      local d = dx * dx + dz * dz
      if d < distance then
        nearest, distance = i, d
      end
    end
    local total = totals[nearest]
    total.mass = total.mass + value.mass
    total.energy = total.energy + value.energy
    total.props = total.props + 1
  end
  return totals
end

plugin.command{
  id = "count",
  title = "Count reclaim per army",
  description = "The mass and energy of the props nearest to each spawn, in the Console and the status line",
  run = function()
    local totals = perArmy()
    if #totals == 0 then
      editor.status("The map has no spawns to count reclaim for")
      return {}
    end
    local parts = {}
    for _, total in ipairs(totals) do
      editor.log(string.format("%s: %d mass and %d energy in %d props",
        total.name, math.floor(total.mass), math.floor(total.energy), total.props))
      parts[#parts + 1] = string.format("%s %d", total.name, math.floor(total.mass))
    end
    editor.status("Mass of reclaim per army: " .. table.concat(parts, ", "))
    return totals
  end,
}

-- The check runs after every step of undo: it counts again only when the props, the markers or
-- the tolerance changed, and else finds what it found the last time.
local checked, found = nil, {}

plugin.check{
  id = "uneven",
  title = "Uneven reclaim between armies",
  run = function()
    local key = map.revision("props") .. "/" .. map.revision("markers") .. "/" .. plugin.value("tolerance")
    if key == checked then
      return found
    end
    checked, found = key, {}
    local totals = perArmy()
    if #totals < 2 then
      return {}
    end
    local sum = 0
    for _, total in ipairs(totals) do
      sum = sum + total.mass
    end
    local average = sum / #totals
    if average <= 0 then
      return {}
    end
    local tolerance = plugin.value("tolerance") / 100
    local problems = found
    for _, total in ipairs(totals) do
      local off = (total.mass - average) / average
      if math.abs(off) > tolerance then
        problems[#problems + 1] = {
          severity = "warning",
          thing = total.ref,
          message = string.format("%s has %d mass of reclaim nearest to it, %d%% %s the average of %d",
            total.name, math.floor(total.mass), math.floor(math.abs(off) * 100), off > 0 and "above" or "below", math.floor(average)),
        }
      end
    end
    return problems
  end,
}
