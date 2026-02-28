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

-- 1. Rojo guarantees this exists based on the JSON file
local BridgeEvent = ReplicatedStorage:WaitForChild("RCL_Event")

-- 2. Logic for commands that run ONLY on the Server
local function handleServerCommand(payload)
    -- Here we can add any server-specific command handling logic.
end

--[=[
    Example Emote ingress payload:

    ```json
    {
        "type": "Emote",
        "targetPlayer": "12345",
        "data": {
            "name": "Dance"
        }
    }
    ```

    But it can also be an array:
    ```json
    [
        {
            "type": "Emote",
            "targetPlayer": "12345",
            "data": {
                "name": "Dance"
            }
        },
        {
            "type": "Emote",
            "targetPlayer": "67890",
            "data": {
                "name": "Wave"
            }
        }
    ]
    ```
]=]

-- 3. The Main Router
local function handleIncomingRequest(input)
    if type(input) ~= "table" then return end

    -- Normalize: If it's a single command (no index 1), wrap it in a table
    -- If it's already an array, use it as is.
    local commands = (input[1] ~= nil) and input or {input}

    for _, payload in ipairs(commands) do
        -- Now we are guaranteed to be looking at individual command objects
        if type(payload) ~= "table" or not payload.type then
            warn("[RCL Ingress] Skipping malformed payload entry")
            continue 
        end

        local target = payload.targetPlayer

        ---------------------------------------------------------
        -- ROUTING LOGIC
        ---------------------------------------------------------
        local targetUserId = tonumber(target) -- Convert to number if it's a string that represents a UserID

        -- A. ROUTE TO SERVER: targetPlayer must be explicitly "Server"
        if target == Enums.Target.Server then
            handleServerCommand(payload)

        -- B. ROUTE TO PLAYER: targetPlayer is a string (username)

        elseif targetUserId then
            -- Use the UserID to find the player object directly
            local player = Players:GetPlayerByUserId(targetUserId)
            
            if player then
                -- Send the specific payload to the client
                BridgeEvent:FireClient(player, payload)
            else
                warn("[RCL Ingress] Player with ID " .. tostring(targetUserId) .. " not found in server.")
            end

        -- C. ERROR HANDLING: No target or invalid target type
        else
            warn(string.format(
                "[RCL Ingress] Dropping payload (Type: %s). Missing or invalid 'targetPlayer'. Received: %s",
                tostring(payload.type),
                tostring(target)
            ))
        end
    end
end

-- 4. Start listening
HttpBridge.registerHandler(handleIncomingRequest)
print("[RCL Ingress] Bridge is active and listening for commands...")