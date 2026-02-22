--!language luau
--[=[
    TeamPayload.lua
    Copyright (c) 2026 Riri a.k.a. Alina Wan <https://github.com/AlinaWan>

    Source: https://github.com/AlinaWan/RobloxChatLauncher/blob/main/assets/integrations/TeamPayload.lua
    
    Licensed under the MPL 2.0 license.
    See https://www.mozilla.org/en-US/MPL/2.0/ for full text.
--]=]

-- !! WARNING: This script is a WORK IN PROGRESS !!
-- The server endpoint is currently under development. 
-- Do not use this in a live game yet.

local HttpService = game:GetService("HttpService")
local Players = game:GetService("Players")

local SERVER_URL = "https://RobloxChatLauncherDemo.onrender.com/team-payload"

local function sendTeamData(player)
    local myTeam = player.Team
    if not myTeam then return end

    -- Get everyone on the same team
    local teammates = {}
    for _, p in ipairs(myTeam:GetPlayers()) do
        table.insert(teammates, {
            name = p.Name,
            userId = p.UserId
        })
    end

    local payload = {
        sender = player.Name,
        teamName = myTeam.Name,
        members = teammates,
        gameId = game.JobId -- Unique ID for this specific match/server
    }

    -- Send to the RobloxChatLauncher server
    local success, result = pcall(function()
        return HttpService:PostAsync(
            SERVER_URL, 
            HttpService:JSONEncode(payload), 
            Enum.HttpContentType.ApplicationJson
        )
    end)

    if success then
        print("Server responded:", result)
    else
        warn("Failed to send team payload:", result)
    end
end

-- Trigger whenever someone joins or changes teams
Players.PlayerAdded:Connect(function(player)
    if player.Team then
        sendTeamData(player)
    end

    player:GetPropertyChangedSignal("Team"):Connect(function()
        sendTeamData(player)
    end)
end)
