--!language luau
--[=[
    Copyright (c) 2026 Riri a.k.a. Alina Wan <https://github.com/AlinaWan>

    Source: https://github.com/AlinaWan/RobloxChatLauncher/blob/main/assets/integrations/
    
    Licensed under the MPL 2.0 license.
    See https://www.mozilla.org/en-US/MPL/2.0/ for full text.
--]=]
-- Use task library over global spawn/wait
local HttpService = game:GetService("HttpService")
local HttpBridge = {}

-------------------------------
-- Base URL for the server.
-- Endpoints will be appended to this unless a full URL is provided.
-------------------------------
local BASE_URL = "https://RobloxChatLauncherDemo.onrender.com"

-- Helper to format URLs consistently, allowing both full URLs and endpoint paths
local function formatUrl(path: string): string
    -- If the path starts with http, use it as is. Otherwise, append to BASE_URL.
    if path:sub(1, 4) == "http" then
        return path
    end
    -- Ensure there is a leading slash
    if path:sub(1, 1) ~= "/" then
        path = "/" .. path
    end
    return BASE_URL .. path
end

-------------------------------
-- Sender (Egress)
-------------------------------
function HttpBridge.send(url: string, payload: table)
    local fullUrl = formatUrl(url)
    task.spawn(function()
        local success, result = pcall(function()
            return HttpService:PostAsync(
                fullUrl,
                HttpService:JSONEncode(payload),
                Enum.HttpContentType.ApplicationJson
            )
        end)
        if not success then warn("[RCL Egress] Error:", result) end
    end)
end

-------------------------------
-- Listener (Ingress)
-------------------------------
local handlers = {} -- Stores all functions that want to hear about commands
local isPolling = false
local DEFAULT_POLL_INTERVAL = 1.0

function HttpBridge.registerHandler(handler: (any) -> ())
    table.insert(handlers, handler)
    
    -- If we aren't polling yet, start the central loop
    if not isPolling then
        isPolling = true
        HttpBridge._startCentralLoop("/ingress/commands", DEFAULT_POLL_INTERVAL)
    end
end

-- Internal loop that only runs ONCE total
function HttpBridge._startCentralLoop(endpoint: string, interval: number)
    local fullUrl = formatUrl(endpoint)
    task.spawn(function()
        while true do
            local success, response = pcall(function() return HttpService:GetAsync(fullUrl) end)
            
            if success and response and #response > 0 then
                local ok, decodedData = pcall(HttpService.JSONDecode, HttpService, response)
                if ok then
                    -- JUST PASS THE DATA. Don't loop here.
                    for _, handlerFunc in ipairs(handlers) do
                        task.spawn(handlerFunc, decodedData)
                    end
                end
            end
            
            if not success then warn("[RCL Ingress] Polling error:", response) end
            task.wait(interval)
        end
    end)
end

return HttpBridge