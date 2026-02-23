--!language luau
--[=[
    Copyright (c) 2026 Riri a.k.a. Alina Wan <https://github.com/AlinaWan>

    Source: https://github.com/AlinaWan/RobloxChatLauncher/blob/main/assets/integrations/
    
    Licensed under the MPL 2.0 license.
    See https://www.mozilla.org/en-US/MPL/2.0/ for full text.
--]=]
--!language luau
--[=[
    Copyright (c) 2026 Riri a.k.a. Alina Wan
    Client-side Dispatcher for RobloxChatLauncher
--]=]

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local Players = game:GetService("Players")

local Enums = require(ReplicatedStorage.Enums)
local BridgeEvent = ReplicatedStorage:WaitForChild("RCL_Event", 10)
if not BridgeEvent then
    warn("[RCL Client] BridgeEvent did not appear in time!")
    return -- Stop the script so it doesn't error later
end
local LocalPlayer = Players.LocalPlayer

-------------------------------
-- 3. Command Handler Map
-------------------------------
local Handlers = {}

-- You can define them directly in the table
Handlers[Enums.CommandType.Emote] = function(data)
    local character = LocalPlayer.Character
    if not character then return end

    local animateScript = character:FindFirstChild("Animate")
    if animateScript and animateScript:IsA("LocalScript") then
        local playEmote = animateScript:FindFirstChild("PlayEmote")
        if playEmote and playEmote:IsA("BindableFunction") then
            task.spawn(function()
                playEmote:Invoke(data.name)
            end)
        else
            warn("[RCL] PlayEmote function not found on this character.")
        end
    end
end

-- Adding a new command is just one block of code:
--[[
Handlers[Enums.CommandType.Kill] = function(data)
    print("Handling kill command for:", data.target)
end
]]

-------------------------------
-- 4. Main Dispatcher (Clean & Dynamic)
-------------------------------
BridgeEvent.OnClientEvent:Connect(function(payload)
    if not payload or type(payload) ~= "table" then return end
    
    local handler = Handlers[payload.type]
    
    if handler then
        handler(payload.data)
    else
        warn("[RCL] No handler defined for command type:", payload.type)
    end
end)
