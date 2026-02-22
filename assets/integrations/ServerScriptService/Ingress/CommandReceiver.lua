--!language luau
--[=[
    Copyright (c) 2026 Riri a.k.a. Alina Wan <https://github.com/AlinaWan>

    Source: https://github.com/AlinaWan/RobloxChatLauncher/blob/main/assets/integrations/
    
    Licensed under the MPL 2.0 license.
    See https://www.mozilla.org/en-US/MPL/2.0/ for full text.
--]=]
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

-- Import our Bridge and Enums
local HttpBridge = require(ReplicatedStorage.HttpBridge)
local Enums = require(ReplicatedStorage.Enums)

-- 1. Ensure the Universal RemoteEvent exists
local BridgeEvent = ReplicatedStorage:FindFirstChild("RCL_Event")
if not BridgeEvent then
    BridgeEvent = Instance.new("RemoteEvent")
    BridgeEvent.Name = "RCL_Event"
    BridgeEvent.Parent = ReplicatedStorage
    print("[RCL Ingress] Initialized universal RCL_Event")
end

-- 2. Logic for commands that run ONLY on the Server
local function handleServerCommand(payload)
    -- Here we can add any server-specific command handling logic.
end

--[[
    Example Emote ingress payload (it should be in square brackets):
    [
        {
            "type": "Emote",
            "targetPlayer": "PlayerName",
            "data": {
                "name": "Dance"
            }
        }
    ]
]]

-- 3. The Main Router
local function handleIncomingRequest(payload)
    -- Check if we have a valid payload
    if not payload or type(payload) ~= "table" then return end

    -- ROUTE TO SERVER
    -- Activates if the ingress payload has no targetPlayer or if the targetPlayer is explicitly the Server
    if payload.targetPlayer == Enums.Target.Server or not payload.targetPlayer then
        handleServerCommand(payload)
    
    -- ROUTE TO PLAYER (Client)
    else
        local target = Players:FindFirstChild(payload.targetPlayer)
        if target then
            -- Send the whole payload so the Client knows what to do
            BridgeEvent:FireClient(target, payload)
        else
            warn("[RCL Ingress] Target player not found in server:", payload.targetPlayer)
        end
    end
end

-- 4. Start listening
HttpBridge.registerHandler(handleIncomingRequest)
print("[RCL Ingress] Bridge is active and listening for commands...")