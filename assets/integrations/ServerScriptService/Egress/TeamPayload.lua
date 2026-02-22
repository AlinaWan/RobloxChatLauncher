--!language luau
--[=[
    Copyright (c) 2026 Riri a.k.a. Alina Wan <https://github.com/AlinaWan>

    Source: https://github.com/AlinaWan/RobloxChatLauncher/blob/main/assets/integrations/
    
    Licensed under the MPL 2.0 license.
    See https://www.mozilla.org/en-US/MPL/2.0/ for full text.
--]=]
local Players = game:GetService("Players")
local HttpBridge = require(game.ReplicatedStorage.HttpBridge)

local ENDPOINT = "/team-payload"

local function sendTeamUpdate(player)
    local myTeam = player.Team
    if not myTeam then return end

    local teammates = {}
    for _, p in ipairs(myTeam:GetPlayers()) do
        table.insert(teammates, { name = p.Name, userId = p.UserId })
    end

    local payload = {
        sender = player.Name,
        teamName = myTeam.Name,
        members = teammates,
        gameId = game.JobId -- Using JobId to identify the specific server instance
    }

    print("[RCL] Sending team update for:", player.Name)
    HttpBridge.send(ENDPOINT, payload)
end

-- Function to set up a player's listeners
local function setupPlayer(player)
    -- Initial check if they are already on a team
    if player.Team then
        sendTeamUpdate(player)
    end

    -- Update whenever their team changes
    player:GetPropertyChangedSignal("Team"):Connect(function()
        sendTeamUpdate(player)
    end)
end

-- 1. Catch players who joined before the script finished loading
for _, player in ipairs(Players:GetPlayers()) do
    setupPlayer(player)
end

-- 2. Watch for new players joining
Players.PlayerAdded:Connect(setupPlayer)