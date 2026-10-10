-- Plateau brush: a tool of the Terrain mode. Press where the terrain has the height to keep, then
-- drag: within the radius the terrain becomes that height, softly towards the edge. A stroke, from
-- press to release, is one step of undo. The options row (and the Plugins page) set the radius and
-- how soft the edge is; the views draw the radius around the mouse. While the symmetry mirrors
-- edits, the handlers are called for every image of the place (keys.image), each keeping its own
-- height.
-- An example of a plugin's tool (see the map editor's guide, "Lua plugins").

local targets = {}

-- Brings the terrain within the radius around a place towards the plateau's height.
local function flatten(x, z, target)
  local radius = plugin.value("radius")
  local soft = plugin.value("softness")
  map.setHeight(x - radius, z - radius, x + radius, z + radius, function(sx, sz, height)
    local distance = math.sqrt((sx - x) ^ 2 + (sz - z) ^ 2)
    if distance > radius then
      return nil
    end
    -- full strength inside, fading over the soft part of the edge
    local edge = radius * (1 - soft)
    local weight = 1
    if distance > edge and radius > edge then
      weight = 1 - (distance - edge) / (radius - edge)
    end
    return height + (target - height) * weight
  end)
end

plugin.tool{
  id = "plateau",
  title = "Plateau",
  mode = "terrain",
  description = "Press at the height to keep, drag to spread it",
  radius = "radius",
  fields = {
    { key = "radius", label = "Plateau radius", type = "number", default = 6, min = 1, max = 32 },
    { key = "softness", label = "Soft edge", type = "number", default = 0.3, min = 0, max = 1,
      help = "The part of the radius over which the plateau fades into the terrain around it" },
  },
  press = function(x, z, keys)
    targets[keys.image] = map.height(x, z)
    flatten(x, z, targets[keys.image])
  end,
  drag = function(x, z, keys)
    flatten(x, z, targets[keys.image] or map.height(x, z))
  end,
}
