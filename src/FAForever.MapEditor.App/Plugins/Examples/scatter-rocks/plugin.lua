-- Scatter rocks: puts rocks at places chosen at random where the terrain is steeper than a slope,
-- as one step of undo, and selects them. The panel sets the slope, how many and which rock; a run
-- from automation or the palette may give them too: { minSlope = 30, count = 10, blueprint = "..." }.
-- An example of the editor's Lua API (see the map editor's guide, "Lua plugins").

plugin.field{ key = "minSlope", label = "Steeper than (degrees)", type = "number", default = 25, min = 0, max = 80 }
plugin.field{ key = "count", label = "Rocks", type = "number", default = 40, min = 1, max = 1000 }
plugin.field{
  key = "blueprint", label = "Rock blueprint", type = "text", default = "",
  help = "A prop's game path, such as /env/evergreen/props/rocks/rock01_prop.bp; empty: the rocks of the workspace's assets",
}

-- The rocks to choose from: the one asked for, else every rock prop of the workspace's assets.
local function rocks(asked)
  if asked ~= nil and asked ~= "" then
    return { asked }
  end
  local found = {}
  for _, asset in ipairs(editor.assets{ kind = "prop", search = "rock", limit = 100 }) do
    if asset.category == "rocks" then
      found[#found + 1] = asset.path
    end
  end
  return found
end

plugin.command{
  id = "scatter",
  title = "Scatter rocks on steep slopes",
  description = "Rocks at places chosen at random where the terrain is steeper than the panel's slope",
  run = function(args)
    local minSlope = args.minSlope or plugin.value("minSlope")
    local count = math.floor(args.count or plugin.value("count"))
    local blueprints = rocks(args.blueprint or plugin.value("blueprint"))
    if #blueprints == 0 then
      error("the workspace has no rock props: set a blueprint in the plugin's panel")
    end
    local width, height = map.size()
    local placed, tries = {}, 0
    while #placed < count and tries < count * 200 do
      tries = tries + 1
      local x, z = math.random() * width, math.random() * height
      if map.slope(x, z) >= minSlope then
        placed[#placed + 1] = map.addProp{
          blueprint = blueprints[math.random(#blueprints)],
          x = x, z = z,
          scale = 0.8 + math.random() * 0.4,
        }
      end
    end
    editor.select(placed)
    editor.status(string.format("Scattered %d rocks on slopes steeper than %d degrees", #placed, minSlope))
    return { placed = #placed }
  end,
}
