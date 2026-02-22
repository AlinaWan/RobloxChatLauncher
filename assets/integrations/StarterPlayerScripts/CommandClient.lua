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
-- 3. Command Handler Functions
-- Define them all as functions here
-------------------------------
local function handleEmote(data)
    local emoteName = data.name
    local character = LocalPlayer.Character
    if not character then return end

    local animateScript = character:FindFirstChild("Animate")
    if animateScript and animateScript:IsA("LocalScript") then
        local playEmote = animateScript:FindFirstChild("PlayEmote")
        
        if playEmote and playEmote:IsA("BindableFunction") then
            print("[RCL] Playing emote:", emoteName)
            playEmote:Invoke(emoteName)
        end
    end
end

-------------------------------
-- 4. Main Dispatcher
-------------------------------
BridgeEvent.OnClientEvent:Connect(function(payload)
    -- Safety check
    if not payload or type(payload) ~= "table" then return end
    
    local cmdType = payload.type
    local data = payload.data

    -- Use the Enums
    if cmdType == Enums.CommandType.Emote and data then
        handleEmote(data)
    end
end)