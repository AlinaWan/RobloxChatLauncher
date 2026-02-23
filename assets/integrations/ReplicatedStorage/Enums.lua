--!language luau
--[=[
    Copyright (c) 2026 Riri a.k.a. Alina Wan <https://github.com/AlinaWan>

    Source: https://github.com/AlinaWan/RobloxChatLauncher/blob/main/assets/integrations/
    
    Licensed under the MPL 2.0 license.
    See https://www.mozilla.org/en-US/MPL/2.0/ for full text.
--]=]
-- enums are sooooo hot omg <3
local function CreateEnum(name, items)
    local enum = {}
    for _, value in ipairs(items) do
        enum[value] = value
    end
    return table.freeze(enum)
end

local Enums = {
    CommandType = CreateEnum("CommandType", {
        "Emote"
    }),
    
    Target = CreateEnum("Target", {
        "Server"
    })
}

return table.freeze(Enums)
