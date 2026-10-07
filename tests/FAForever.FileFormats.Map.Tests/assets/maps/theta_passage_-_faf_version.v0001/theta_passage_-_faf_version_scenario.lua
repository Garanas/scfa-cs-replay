version = 3 -- Lua Version. Dont touch this
ScenarioInfo = {
    name = "Theta Passage - FAF version",
    description = "In the early days of the settlement, wayfarers knew to go through the arch and head due north to reach civilization. These days, the Passage is much more dangerous, thanks to the constant fighting that moves back and forth across the desert. - FAF version of the original FA map 'SCMAP_012': Ensures symmetrical heightmap, marker, props and units. - Modified by M&M",
    preview = '',
    map_version = 1,
    type = 'skirmish',
    starts = true,
    size = {256, 256},
    reclaim = {4271.7, 1976},
    map = '/maps/theta_passage_-_faf_version.v0001/theta_passage_-_faf_version.scmap',
    save = '/maps/theta_passage_-_faf_version.v0001/theta_passage_-_faf_version_save.lua',
    script = '/maps/theta_passage_-_faf_version.v0001/theta_passage_-_faf_version_script.lua',
    norushradius = 70,
    Configurations = {
        ['standard'] = {
            teams = {
                {
                    name = 'FFA',
                    armies = {'ARMY_1', 'ARMY_2'}
                },
            },
            customprops = {
                ['ExtraArmies'] = STRING( 'ARMY_17 NEUTRAL_CIVILIAN' ),
            },
        },
    },
}
